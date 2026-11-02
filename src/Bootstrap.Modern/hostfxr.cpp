#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <string>
#include <vector>
#include <algorithm>

// An app-local forwarding bridge. Only explicitly registered PCL apphosts get
// a startup hook. The actual Microsoft hostfxr still resolves and runs .NET.
static HMODULE real_host;
static std::wstring dotnet_root;
static std::wstring base;
static std::wstring read_file(const std::wstring& path) {
    HANDLE f = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ|FILE_SHARE_WRITE|FILE_SHARE_DELETE, 0, OPEN_EXISTING, 0, 0);
    if (f == INVALID_HANDLE_VALUE) return L"";
    DWORD n = GetFileSize(f, 0), got = 0;
    if (n > 16384) { CloseHandle(f); return L""; }
    std::string bytes(n, '\0'); ReadFile(f, &bytes[0], n, &got, 0); CloseHandle(f);
    int length = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, bytes.c_str(), got, 0, 0);
    std::wstring result(length, L'\0'); MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, bytes.c_str(), got, &result[0], length);
    while (!result.empty() && (result.back() == L'\r' || result.back() == L'\n')) result.pop_back();
    return result;
}
static std::wstring env(const wchar_t* key) {
    DWORD n = GetEnvironmentVariableW(key, 0, 0); if (!n) return L"";
    std::wstring value(n, L'\0'); GetEnvironmentVariableW(key, &value[0], n); value.resize(n-1); return value;
}
static bool exists(const std::wstring& path) { return GetFileAttributesW(path.c_str()) != INVALID_FILE_ATTRIBUTES; }
static HMODULE find_host(const std::wstring& root) {
    if (root.empty()) return 0;
    WIN32_FIND_DATAW data; std::wstring fxr = root + L"\\host\\fxr\\";
    HANDLE search = FindFirstFileW((fxr + L"*").c_str(), &data); if (search == INVALID_HANDLE_VALUE) return 0;
    struct version { int a,b,c; std::wstring dir; };
    std::vector<version> candidates;
    do { int a=0,b=0,c=0; wchar_t extra=0;
        if ((data.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) && swscanf_s(data.cFileName,L"%d.%d.%d%c",&a,&b,&c,&extra,1)==3)
            candidates.push_back({a,b,c,data.cFileName});
    } while (FindNextFileW(search, &data)); FindClose(search);
    std::sort(candidates.begin(), candidates.end(), [](const version& a,const version& b) { if(a.a!=b.a)return a.a>b.a;if(a.b!=b.b)return a.b>b.b;return a.c>b.c; });
    for (const auto& v : candidates) {
        HMODULE h = LoadLibraryExW((fxr + v.dir + L"\\hostfxr.dll").c_str(),0,LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR|LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
        if (h) { dotnet_root=root; return h; }
    } return 0;
}
static BOOL CALLBACK initialize(PINIT_ONCE, PVOID, PVOID*) {
    wchar_t path[32768]; GetModuleFileNameW(0,path,32768); std::wstring exe(path);
    auto slash=exe.find_last_of(L"\\/"); base=exe.substr(0,slash);
    // An optional private runtime is a local installer setting, never shipped
    // with an absolute developer path in the public package.
    std::vector<std::wstring> roots {read_file(base+L"\\PCL2-download-repair\\runtime\\dotnet-root.txt"),env(L"DOTNET_ROOT_X64"),env(L"DOTNET_ROOT")};
    wchar_t regpath[32768]; DWORD size=sizeof(regpath);
    if (RegGetValueW(HKEY_LOCAL_MACHINE,L"SOFTWARE\\dotnet\\Setup\\InstalledVersions\\x64",L"InstallLocation",RRF_RT_REG_SZ,0,regpath,&size)==ERROR_SUCCESS) roots.push_back(regpath);
    roots.push_back(env(L"ProgramFiles")+L"\\dotnet");
    for(auto root:roots) if ((real_host=find_host(root))!=0) break;
    return TRUE;
}
static FARPROC symbol(const char* name) {
    static INIT_ONCE once=INIT_ONCE_STATIC_INIT; InitOnceExecuteOnce(&once,initialize,0,0);
    return real_host ? GetProcAddress(real_host,name) : 0;
}
static void prepare(const wchar_t* host) {
    std::wstring exe(host?host:L""); auto at=exe.find_last_of(L"\\/"); std::wstring name=exe.substr(at==std::wstring::npos?0:at+1);
    std::wstring registrations=read_file(base+L"\\PCL2-download-repair\\runtime\\modern-targets.txt")+L"\n";
    registrations.erase(std::remove(registrations.begin(),registrations.end(),L'\r'),registrations.end());
    bool registered=false;size_t begin=0;
    while(begin<registrations.size()) {size_t end=registrations.find(L'\n',begin);if(end==std::wstring::npos)break;
        if(CompareStringOrdinal(registrations.c_str()+begin,(int)(end-begin),name.c_str(),(int)name.size(),TRUE)==CSTR_EQUAL){registered=true;break;}begin=end+1;}
    if (!registered) return;
    std::wstring hook=base+L"\\PCL2-download-repair\\runtime\\bootstrap\\PclRepair.StartupHook.dll";
    if (!exists(hook)) return;
    auto previous=env(L"DOTNET_STARTUP_HOOKS");
    SetEnvironmentVariableW(L"PCL_REPAIR_PREVIOUS_HOOKS",previous.c_str());
    auto hooks=previous.empty()?hook:previous+L";"+hook;
    SetEnvironmentVariableW(L"DOTNET_STARTUP_HOOKS",hooks.c_str());
}
extern "C" __declspec(dllexport) int __cdecl hostfxr_main_bundle_startupinfo(int argc,const wchar_t** argv,const wchar_t* host,const wchar_t*,const wchar_t* app,long long offset) {
    using fn=int(__cdecl*)(int,const wchar_t**,const wchar_t*,const wchar_t*,const wchar_t*,long long);
    auto f=(fn)symbol("hostfxr_main_bundle_startupinfo"); if(!f)return (int)0x80008083;prepare(host);
    return f(argc,argv,host,dotnet_root.c_str(),app,offset);
}
extern "C" __declspec(dllexport) int __cdecl hostfxr_main_startupinfo(int argc,const wchar_t** argv,const wchar_t* host,const wchar_t*,const wchar_t* app) {
    using fn=int(__cdecl*)(int,const wchar_t**,const wchar_t*,const wchar_t*,const wchar_t*);
    auto f=(fn)symbol("hostfxr_main_startupinfo");if(!f)return (int)0x80008083;prepare(host);return f(argc,argv,host,dotnet_root.c_str(),app);
}
extern "C" __declspec(dllexport) void* __cdecl hostfxr_set_error_writer(void* writer) {
    using fn=void*(__cdecl*)(void*);auto f=(fn)symbol("hostfxr_set_error_writer");return f?f(writer):0;
}
extern "C" __declspec(dllexport) int __cdecl hostfxr_main(int argc,const wchar_t** argv) {
    using fn=int(__cdecl*)(int,const wchar_t**);auto f=(fn)symbol("hostfxr_main");return f?f(argc,argv):(int)0x80008083;
}
