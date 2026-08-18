
#include <windows.h>
#include <winhttp.h>
#include <intrin.h>
#include <cstdint>
#include <cstdio>
#include <cstdarg>
#include <cstdlib>
#include <cstring>
#include <utility>
#include <algorithm>
#include <string>
#include <vector>
#include <map>
#include <set>
#include <fstream>
#include <iterator>
#include <cctype>
#include <cwctype>

#include "MinHook.h"
#include "json.hpp"

#pragma comment(lib, "winhttp.lib")

using json = nlohmann::json;

#define RVA_PROPERTY_DISPATCH     0x170DEA0
#define RVA_GETPROTOIDFROMENUM    0x1719A70
#define RVA_LOOKUPPROTORECORD     0x160A500
#define RVA_GETPROTODATAREFRECORD 0x160A5B0
#define RVA_PACKAGELOOP           0x1432A10
#define RVA_GETLOADEDUNREALCLASS  0x1421790

#define RVA_POWERGETUNREALCLASS   0x1592C80

#define RVA_CONDGETUNREALCLASS    0x1592B80

#define RVA_CREATEMISSILE         0x1A07340

#define RVA_HOTSPOTGETCLASS       0x149CCF0
#define HS_WORLDENTITY_OFF        0x8

#define RVA_PROJECTILEGETCLASS    0x149C870

#define RVA_SETAUTHTICKET         0x19B4510
#define LOGINMGR_AUTHTICKET_OFF   0x538

#define AUTHTICKET_SESSIONID_OFF  0x20

#define REGISTER_SESSION_WAIT_MS  600000
#define REGISTER_SESSION_POLL_MS  250

static void* volatile g_LocalAvatar = nullptr;

#define HS_AVATAR_CORR_HI         0x800

#define HS_SCAN_LO                0x000

#define HS_SCAN_HI                0x800

#define MP_OWNER_OFF              0x18

#define OWNER_SCAN_LO             0x600
#define OWNER_SCAN_HI             0xA00

#define OWNER_COSTUME_OFF         0x788

#define RVA_FIREPROJECTILE        0x14A6860

#define FP_PROJECTILECLASS_OFF    0x170

#define RVA_GETPROTOFROMENUMVALUE 0x158F570
#define RVA_GETPROTOENUMVALUE     0x158F370
#define RVA_VERIFYFAIL            0x5A0940
#define RVA_GETICONPATH           0x1587090

#define RVA_GETLOCALESTRING       0x18BB160

#define RVA_LOCALEMGR_SINGLETON   0x1689F40

#define RVA_ADDDATAREF            0x15663A0
#define RVA_ASSETCACHE_GLOBAL     0x2C5C0E8
#define ASSETCACHE_MGR_OFFSET     0xF8
#define RVA_STRINGT_ALLOCATOR     0x2579818

#define RVA_PACKAGELOADER         0x142EE60

#define RVA_FNAME_INIT_ANSI       0x79CFA0
#define RVA_STATICFINDOBJECT      0x7B0640
#define RVA_UCLASS_STATICCLASS    0x2B186F8
#define RVA_NUMNAMES              0x2C23B48

#define RVA_FINDCACHEDPKGINFO     0x1322DD0
#define ASSETCACHE_PKGINFO_MAP    0x278

#define MSG_SET_PROPERTY      0x92
#define PROP_COSTUME_CURRENT  207

#define FNAME_Add   1
#define FNAME_Find  0

struct EffectRedirect {
    uint64_t     From = 0;
    uint64_t     To = 0;
    std::string  Package;
    std::string  Class;
    void* volatile CachedClass = nullptr;

    void* volatile StockClass = nullptr;

    bool volatile Answered = false;
};

struct CostumeMod {
    std::string  Name;
    uint32_t     EnumIndex = 0;
    uint64_t     CustomID = 0;
    uint64_t     DonorID = 0;
    uint64_t     DonorAsset = 0;

    uint64_t     ForgedAsset = 0;

    std::map<uint64_t, EffectRedirect> Effects;

    struct HotspotRef {
        uint64_t Forged = 0;
        uint64_t Stock = 0;
        uint32_t Enum = 0;
    };
    std::vector<HotspotRef> Hotspots;

    std::string  DonorClass;
    std::wstring ClassPath;
    std::wstring ClassPathLower;
    std::vector<std::string> Chain;

    std::vector<uint64_t> ChainFNames;
    uint64_t     DonorPkgFName = 0;
    void* volatile CachedClass = nullptr;
    bool Interned = false;

    std::vector<std::pair<uint32_t, uint64_t>> ProtoPatches;
    void* volatile ClonedRecord = nullptr;
};

static std::map<uint32_t, CostumeMod>  g_ByEnum;
static std::map<uint64_t, CostumeMod*> g_ByCustom;

static std::map<uint64_t, CostumeMod*> g_ByForgedAsset;

static std::map<uint64_t, EffectRedirect*> g_EffectByForgedAsset;

static std::map<uint64_t, std::vector<EffectRedirect*>> g_EffectByPkgFName;

struct FxStockRef { CostumeMod* Mod = nullptr; EffectRedirect* Fx = nullptr; };
static std::map<uint64_t, FxStockRef> g_EffectByStockAsset;

static bool g_FxDryRun = false;

static bool g_FxHeroKeyConditions = false;

static thread_local CostumeMod* t_MissileMod = nullptr;

static thread_local CostumeMod* t_HotspotMod = nullptr;

static inline CostumeMod* CurrentFxOwner()
{
    return t_MissileMod ? t_MissileMod : t_HotspotMod;
}

static bool g_MissileProbe = false;

#define HS7_SEEN_MAX 64
static volatile long g_Hs7SeenN = 0;
static uint64_t      g_Hs7Seen[HS7_SEEN_MAX] = { 0 };
static volatile long g_Hs5SeenN = 0;
static uint64_t      g_Hs5Seen[HS7_SEEN_MAX] = { 0 };

static bool SeenOnce(volatile long* count, uint64_t* seen, uint64_t id)
{
    long cnt = *count;
    if (cnt >= HS7_SEEN_MAX) return false;
    for (long i = 0; i < cnt && i < HS7_SEEN_MAX; ++i)
        if (seen[i] == id) return false;
    long slot = InterlockedIncrement(count) - 1;
    if (slot >= HS7_SEEN_MAX) return false;
    seen[slot] = id;
    return true;
}

static volatile long g_Hs2SeenN = 0;
static uint64_t      g_Hs2Seen[HS7_SEEN_MAX] = { 0 };

static bool Hs7FirstTime(uint64_t id) { return SeenOnce(&g_Hs7SeenN, g_Hs7Seen, id); }
static bool Hs5FirstTime(uint64_t id) { return SeenOnce(&g_Hs5SeenN, g_Hs5Seen, id); }
static bool Hs2FirstTime(uint64_t id) { return SeenOnce(&g_Hs2SeenN, g_Hs2Seen, id); }

static std::unordered_map<uint32_t, uint64_t> g_ByHotspotEnum;

struct ForgedHotspot {
    CostumeMod* Mod = nullptr;
    uint64_t    Stock = 0;
};
static std::unordered_map<uint64_t, ForgedHotspot> g_ByForgedHotspot;

static bool g_HotspotFx = true;

static bool        g_Register     = true;
static std::string g_RegisterHost;
static int         g_RegisterPort = 0;

static const int kDefaultRegisterPort = 8080;

static const int kAltRegisterPort = 9090;

static bool g_PerAvatarMesh = false;

static std::set<uint64_t> g_FxStackIds;
static int                g_FxStackIdHits = 0;

static std::map<uint64_t, std::string> g_IconPaths;

static std::map<uint64_t, std::string> g_NameTexts;

static std::map<uint64_t, void*> g_NameEntries;

static unsigned char g_NameTemplate[0x80];
static volatile LONG g_NameTemplateReady = 0;

static uint64_t ForgeNameId(uint32_t costumeEnum)
{
    return (0xC057ULL << 48) | ((uint64_t)(costumeEnum - 100000u) << 16) | 0x0505ULL;
}

static uint64_t ForgeCostumeAssetId(uint32_t costumeEnum)
{
    return (0xC057ULL << 48) | ((uint64_t)(costumeEnum - 100000u) << 24) | 0x1299ULL;
}

static uint64_t ForgeEffectAssetId(uint32_t costumeEnum, uint32_t effectIndex, uint64_t fromAsset)
{
    return (0xC057ULL << 48)
         | ((uint64_t)(costumeEnum - 100000u) << 40)
         | ((uint64_t)(effectIndex & 0xFFu) << 32)
         | (fromAsset & 0xFFFFULL);
}

static std::vector<std::string> g_IconPackages;
static std::vector<uint64_t>    g_IconPackageFNames;
static volatile LONG            g_IconPackagesLoaded = 0;

static volatile LONG            g_IconUiAlive = 0;

static std::map<std::string, uint64_t> g_DonorAssets;

static CostumeMod* volatile g_ActiveMod = nullptr;

static std::set<uint64_t> g_LoadedPackages;

static uint8_t* g_Base = nullptr;
static std::wstring     g_GameDir;
static CRITICAL_SECTION g_LogLock;

typedef uint64_t(__fastcall* PropDispatch_t)(void*, int, void*);
typedef uint64_t* (__fastcall* GetProtoIdFromEnum_t)(uint64_t*, uint64_t, uint32_t);
typedef void* (__fastcall* LookupProtoRecord_t)(void*, void*, void*);

typedef void(__fastcall* PackageLoop_t)(void*, uint64_t*, int, char);

typedef char(__fastcall* PackageLoader_t)(void*, int, void*, int, void*, void*);
typedef void* (__fastcall* GetLoadedUnrealClass_t)(void*, uint64_t*);

typedef void(__fastcall* FNameInitA_t)(void*, const char*, int, int);
typedef void* (__fastcall* StaticFindObject_t)(void*, void*, const wchar_t*, int);

typedef void* (__fastcall* FindCachedPkgInfo_t)(void*, uint64_t);

typedef uint64_t* (__fastcall* GetProtoFromEnumValue_t)(void*, uint64_t*, int, int);

typedef uint32_t (__fastcall* GetProtoEnumValue_t)(void*, uint64_t*, uint32_t);

typedef void* (__fastcall* GetProtoDataRefRecord_t)(void*, uint64_t*);

typedef void* (__fastcall* VerifyFail_t)(const char*, const char*, uint32_t, void*, const char*, int);

typedef void* (__fastcall* GetIconPath_t)(void*, void*, uint32_t);

typedef void* (__fastcall* GetLocaleString_t)(void*, uint64_t, char);
typedef void* (__fastcall* LocaleMgrSingleton_t)();

static PropDispatch_t         OrigPropDispatch = nullptr;
static GetProtoIdFromEnum_t   OrigGetProtoId = nullptr;
static LookupProtoRecord_t    OrigLookupProto = nullptr;
static PackageLoop_t          OrigLoop = nullptr;
static GetLoadedUnrealClass_t OrigGLUC = nullptr;
static FindCachedPkgInfo_t    OrigFindPkgInfo = nullptr;
static GetProtoFromEnumValue_t OrigGetProtoFromEnum = nullptr;
static GetProtoEnumValue_t     OrigGetProtoEnumValue = nullptr;
static GetProtoDataRefRecord_t OrigGetProtoDataRefRec = nullptr;
static VerifyFail_t            OrigVerifyFail = nullptr;
static GetIconPath_t           OrigGetIconPath = nullptr;
static GetLocaleString_t       OrigGetLocaleString = nullptr;

static PackageLoader_t     pLoader = nullptr;
static FNameInitA_t        pFNameInit = nullptr;
static StaticFindObject_t  pStaticFindObject = nullptr;

static __declspec(thread) int t_InLoop = 0;

static bool g_LogTruncated = false;

static std::wstring g_LogPath;

static void ResolveLogPathOnce()
{
    if (!g_LogPath.empty()) return;

    std::wstring key = L"MHCostumeModLog_" + g_GameDir;
    for (size_t i = 0; i < key.size(); ++i)
        if (key[i] == L'\\' || key[i] == L'/' || key[i] == L':') key[i] = L'_';
    if (key.size() > 200) key.resize(200);

    HANDLE h = CreateMutexW(nullptr, TRUE, key.c_str());
    const bool notFirst = (h != nullptr && GetLastError() == ERROR_ALREADY_EXISTS);

    if (notFirst) {

        g_LogPath = g_GameDir + L"\\CostumeMod." +
                    std::to_wstring((unsigned long)GetCurrentProcessId()) + L".log";
    } else {
        g_LogPath = g_GameDir + L"\\CostumeMod.log";
    }
}

static void PruneOldPidLogs() {
    const size_t KEEP = 2;

    std::vector<std::pair<ULONGLONG, std::wstring>> found;
    WIN32_FIND_DATAW fd;
    HANDLE h = FindFirstFileW((g_GameDir + L"\\CostumeMod.*.log").c_str(), &fd);
    if (h == INVALID_HANDLE_VALUE) return;
    do {
        if (fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) continue;

        std::wstring name = fd.cFileName;
        if (name == L"CostumeMod.prev.log") continue;

        std::wstring full = g_GameDir + L"\\" + name;
        if (full == g_LogPath) continue;

        ULARGE_INTEGER t;
        t.LowPart  = fd.ftLastWriteTime.dwLowDateTime;
        t.HighPart = fd.ftLastWriteTime.dwHighDateTime;
        found.push_back(std::make_pair(t.QuadPart, full));
    } while (FindNextFileW(h, &fd));
    FindClose(h);

    if (found.size() <= KEEP) return;
    std::sort(found.begin(), found.end());
    for (size_t i = 0; i + KEEP < found.size(); ++i)
        (void)_wremove(found[i].second.c_str());
}

static void WriteLog(const char* text) {
    EnterCriticalSection(&g_LogLock);
    ResolveLogPathOnce();
    const std::wstring& path = g_LogPath;

    const wchar_t* mode = L"a";
    if (!g_LogTruncated) {
        g_LogTruncated = true;
        PruneOldPidLogs();

        if (path.find(L"CostumeMod.log") != std::wstring::npos) {
            std::wstring prev = g_GameDir + L"\\CostumeMod.prev.log";

            (void)_wremove(prev.c_str());
            (void)_wrename(path.c_str(), prev.c_str());
        }
        mode = L"w";
    }

    FILE* f = nullptr;
    if (_wfopen_s(&f, path.c_str(), mode) == 0 && f) {
        SYSTEMTIME st; GetLocalTime(&st);
        fprintf(f, "[%02d:%02d:%02d.%03d] %s",
            st.wHour, st.wMinute, st.wSecond, st.wMilliseconds, text);
        fclose(f);
    }
    LeaveCriticalSection(&g_LogLock);
}

static void WriteLogF(const char* fmt, ...) {
    char buf[2048];
    va_list ap; va_start(ap, fmt);
    vsnprintf(buf, sizeof(buf), fmt, ap);
    va_end(ap);
    WriteLog(buf);
}

static bool                  g_SafeMode = true;
static std::set<std::string> g_Quarantine;

static bool g_Diagnostics = false;

static void WriteDiagF(const char* fmt, ...) {
    if (!g_Diagnostics) return;
    char buf[2048];
    va_list ap; va_start(ap, fmt);
    vsnprintf(buf, sizeof(buf), fmt, ap);
    va_end(ap);
    WriteLog(buf);
}

static std::wstring SafetyPath(const wchar_t* leaf) { return g_GameDir + L"\\" + leaf; }

static std::string ToLowerAscii(const std::string& s) {
    std::string r = s;
    for (size_t i = 0; i < r.size(); ++i)
        if (r[i] >= 'A' && r[i] <= 'Z') r[i] = (char)(r[i] - 'A' + 'a');
    return r;
}

static std::set<std::string> g_Unavailable;

static bool                 g_BandScan = false;

#define PHASE0_ASSET_PROBE 0

#define PHASE0_POWER_PROBE 0

static bool                 g_AssetProbe = false;
#if PHASE0_ASSET_PROBE
static std::set<uint64_t>   g_ProbeSeen;
static const size_t         PROBE_MAX = 4000;

static void ProbeAsset(uint64_t id, void* resolved)
{
    if (!g_AssetProbe || id == 0) return;
    if (g_ProbeSeen.size() >= PROBE_MAX) return;
    if (!g_ProbeSeen.insert(id).second) return;

    WriteLogF("[PROBE] #%04llu  GLUC asset=0x%016llX  -> %s\r\n",
        (unsigned long long)g_ProbeSeen.size(), (unsigned long long)id,
        resolved ? "class" : "NULL");

    if (g_ProbeSeen.size() == PROBE_MAX)
        WriteLog("[PROBE] limit reached - further distinct assets suppressed.\r\n");
}
#endif

#if PHASE0_POWER_PROBE
static bool               g_PowerProbe = false;
static std::set<void*>    g_PowerSeen;
static const size_t       POWERPROBE_MAX = 600;

#endif

static bool IsQuarantined(const std::string& name) {
    std::string key = ToLowerAscii(name);
    if (g_Unavailable.find(key) != g_Unavailable.end()) return true;
    return g_SafeMode && g_Quarantine.find(key) != g_Quarantine.end();
}

static std::wstring g_CookedDir;

static bool PackageFileExists(const std::string& fnameLower)
{
    if (g_CookedDir.empty() || fnameLower.empty()) return false;

    std::wstring path = g_CookedDir + L"\\";
    for (size_t i = 0; i < fnameLower.size(); ++i)
        path += (wchar_t)(unsigned char)fnameLower[i];
    path += L".upk";

    DWORD attr = GetFileAttributesW(path.c_str());
    return attr != INVALID_FILE_ATTRIBUTES && !(attr & FILE_ATTRIBUTE_DIRECTORY);
}

static void WriteArmingSentinel(const std::string& name) {
    if (!g_SafeMode) return;
    std::wstring path = SafetyPath(L"CostumeMod.arming");
    FILE* f = nullptr;
    if (_wfopen_s(&f, path.c_str(), L"w") == 0 && f) {
        fprintf(f, "%s\n", name.c_str());
        fclose(f);
    }
}

static void ClearArmingSentinel() {
    if (!g_SafeMode) return;
    DeleteFileW(SafetyPath(L"CostumeMod.arming").c_str());
}

static void LoadQuarantineAndRotateSentinel() {
    if (!g_SafeMode) return;

    std::wstring sentinel = SafetyPath(L"CostumeMod.arming");
    std::wstring quarantine = SafetyPath(L"CostumeMod.quarantine");

    char crashed[256] = { 0 };
    FILE* f = nullptr;
    if (_wfopen_s(&f, sentinel.c_str(), L"r") == 0 && f) {
        if (fgets(crashed, sizeof(crashed), f)) {
            size_t n = strlen(crashed);
            while (n && (crashed[n - 1] == '\n' || crashed[n - 1] == '\r')) crashed[--n] = '\0';
        }
        fclose(f);
        DeleteFileW(sentinel.c_str());

        if (crashed[0]) {
            FILE* q = nullptr;
            if (_wfopen_s(&q, quarantine.c_str(), L"a") == 0 && q) {
                fprintf(q, "%s\n", crashed);
                fclose(q);
            }
            WriteLogF("\r\n"
                "*** QUARANTINE ***********************************************\r\n"
                "*** \"%s\" was loading when the client died last session.\r\n"
                "*** It is DISABLED this run; every other costume still works.\r\n"
                "*** Re-enable by removing its line from CostumeMod.quarantine\r\n"
                "**************************************************************\r\n\r\n",
                crashed);
        }
    }

    if (_wfopen_s(&f, quarantine.c_str(), L"r") == 0 && f) {
        char line[256];
        while (fgets(line, sizeof(line), f)) {
            size_t n = strlen(line);
            while (n && (line[n - 1] == '\n' || line[n - 1] == '\r')) line[--n] = '\0';
            if (line[0]) g_Quarantine.insert(ToLowerAscii(line));
        }
        fclose(f);
    }

    if (!g_Quarantine.empty())
        WriteLogF("[safe] %zu costume(s) quarantined - see CostumeMod.quarantine\r\n",
            g_Quarantine.size());
}

static uint64_t MakeFName(const char* name, int findType) {
    int out[2] = { 0, 0 };
    pFNameInit(out, name, 0, findType);
    return *(uint64_t*)out;
}

static int ReadNumNames() {
    int v = 0;
    __try { v = *(int*)(g_Base + RVA_NUMNAMES); }
    __except (EXCEPTION_EXECUTE_HANDLER) { v = -1; }
    return v;
}

static void InternChain(CostumeMod* mod) {
    if (mod->Interned) return;

    WriteDiagF("[intern] \"%s\" - %zu package(s), load order:\r\n",
        mod->Name.c_str(), mod->Chain.size());

    mod->ChainFNames.clear();
    for (size_t i = 0; i < mod->Chain.size(); ++i) {
        int before = ReadNumNames();
        uint64_t fn = MakeFName(mod->Chain[i].c_str(), FNAME_Add);
        int after = ReadNumNames();

        mod->ChainFNames.push_back(fn);

        bool isFx = false;
        int nFx = 0;
        for (auto& e : mod->Effects) {
            if (e.second.Package.empty() || e.second.Package != mod->Chain[i]) continue;
            isFx = true;
            std::vector<EffectRedirect*>& vec = g_EffectByPkgFName[fn];
            bool already = false;
            for (size_t k = 0; k < vec.size(); ++k)
                if (vec[k] == &e.second) { already = true; break; }
            if (!already) { vec.push_back(&e.second); ++nFx; }
        }

        char fxTag[48];
        if (nFx > 1) sprintf_s(fxTag, "  [fx x%d]", nFx);
        else         sprintf_s(fxTag, "%s", isFx ? "  [fx]" : "");
        WriteDiagF("           [%zu] \"%s\" -> 0x%016llX  NumNames %d->%d%s%s\r\n",
            i, mod->Chain[i].c_str(), (unsigned long long)fn,
            before, after,
            (after > before) ? " (NEW - interned)" : " (existed)",
            fxTag);
    }

    {
        if (mod->DonorClass.empty()) {

            WriteLogF("           [donor-pkg] *** \"%s\" HAS NO donorClass - Hook 4 "
                "substitution DISABLED for this costume. Add \"donorClass\" to its entry "
                "in CustomCostumes.json.\r\n", mod->Name.c_str());
            mod->DonorPkgFName = 0;
        }
        else {
            std::string dp = "uc__" + mod->DonorClass + "_sf";
            for (char& c : dp) c = (char)tolower((unsigned char)c);
            mod->DonorPkgFName = MakeFName(dp.c_str(), FNAME_Find);
            WriteLogF("           [donor-pkg] \"%s\" -> 0x%016llX%s\r\n",
                dp.c_str(), (unsigned long long)mod->DonorPkgFName,
                mod->DonorPkgFName ? "" : "  (NOT FOUND - Hook 4 will no-op!)");
        }
    }

    mod->Interned = true;
}

static inline uint64_t Reverse64(uint64_t v) {
    v = ((v & 0x5555555555555555ULL) << 1) | ((v >> 1) & 0x5555555555555555ULL);
    v = ((v & 0x3333333333333333ULL) << 2) | ((v >> 2) & 0x3333333333333333ULL);
    v = ((v & 0x0F0F0F0F0F0F0F0FULL) << 4) | ((v >> 4) & 0x0F0F0F0F0F0F0F0FULL);
    return _byteswap_uint64(v);
}

static uint64_t __fastcall MyPropDispatch(void* self, int msgType, void* msg)
{
    if (msgType == MSG_SET_PROPERTY && msg) {
        uint64_t rawId = 0, value = 0;
        bool ok = false;

        __try {
            rawId = *(uint64_t*)((uint8_t*)msg + 0x18);
            value = *(uint64_t*)((uint8_t*)msg + 0x20);
            ok = true;
        }
        __except (EXCEPTION_EXECUTE_HANDLER) { ok = false; }

        if (ok) {
            uint32_t propEnum = (uint32_t)(Reverse64(rawId) >> 53);

            if (propEnum == PROP_COSTUME_CURRENT) {
                uint32_t costumeEnum = (uint32_t)value;
                auto it = g_ByEnum.find(costumeEnum);

                if (it != g_ByEnum.end() && IsQuarantined(it->second.Name)) {

                    WriteLogF("=== [H0] QUARANTINED  enum %u -> \"%s\" - NOT armed "
                        "(donor will render) ===\r\n",
                        costumeEnum, it->second.Name.c_str());
                    g_ActiveMod = nullptr;
                }
                else if (it != g_ByEnum.end()) {
                    g_ActiveMod = &it->second;
                    InternChain(g_ActiveMod);
                    WriteLogF("\r\n=== [H0] ARM  costume enum %u -> \"%s\" ===\r\n",
                        costumeEnum, g_ActiveMod->Name.c_str());
                }
                else if (g_ActiveMod) {
                    WriteLogF("=== [H0] DISARM  costume enum %u is stock ===\r\n",
                        costumeEnum);
                    g_ActiveMod = nullptr;
                }
            }
        }
    }

    return OrigPropDispatch(self, msgType, msg);
}

static void* Hook1_Trampoline = nullptr;

typedef uint64_t* (__fastcall* GetProtoId3_t)(uint64_t*, uint64_t, uint32_t);

static uint64_t* __fastcall MyGetProtoIdFromEnum(
    uint64_t* outId, uint64_t value, uint32_t typeSelector)
{

    uint32_t enumIndex = (uint32_t)value;
    auto it = g_ByEnum.find(enumIndex);
    if (it != g_ByEnum.end()) {
        *outId = it->second.CustomID;
        return outId;
    }

    return ((GetProtoId3_t)Hook1_Trampoline)(outId, value, typeSelector);
}

static uint64_t* __fastcall MyGetProtoFromEnumValue(
    void* self, uint64_t* outId, int enumValue, int prototypeClassId)
{

    if (enumValue >= 100000 && outId != nullptr) {

        if (!g_ByHotspotEnum.empty()) {
            auto hs = g_ByHotspotEnum.find((uint32_t)enumValue);
            if (hs != g_ByHotspotEnum.end()) {

                if (g_MissileProbe && Hs5FirstTime(hs->second))
                    WriteDiagF("[HS5] enum %d (classId %d) -> forged hotspot 0x%016llX\r\n",
                        enumValue, prototypeClassId, (unsigned long long)hs->second);
                *outId = hs->second;
                return outId;
            }
        }

        auto it = g_ByEnum.find((uint32_t)enumValue);
        if (it != g_ByEnum.end()) {

            static int s_logged = 0;
            if (s_logged < 20) {
                ++s_logged;
                WriteDiagF("[H5] enum %d (classId %d) -> custom 0x%016llX  \"%s\"%s\r\n",
                    enumValue, prototypeClassId,
                    (unsigned long long)it->second.CustomID, it->second.Name.c_str(),
                    (s_logged == 20) ? "  (further H5 logging suppressed)" : "");
            }

            *outId = it->second.CustomID;
            return outId;
        }
    }

    return OrigGetProtoFromEnum(self, outId, enumValue, prototypeClassId);
}

static uint32_t __fastcall MyGetProtoEnumValue(void* self, uint64_t* protoIdPtr, uint32_t prototypeClassId)
{
    if (protoIdPtr) {
        uint64_t id = 0;
        bool ok = false;
        __try { id = *protoIdPtr; ok = true; }
        __except (EXCEPTION_EXECUTE_HANDLER) { ok = false; }

        if (ok) {
            auto it = g_ByCustom.find(id);
            if (it != g_ByCustom.end()) {
                static int s_logged6 = 0;
                if (s_logged6 < 20) {
                    ++s_logged6;
                    WriteDiagF("[H6] id 0x%016llX (classId %u) -> enum %u  \"%s\"%s\r\n",
                        (unsigned long long)id, prototypeClassId,
                        it->second->EnumIndex, it->second->Name.c_str(),
                        (s_logged6 == 20) ? "  (further H6 logging suppressed)" : "");
                }
                return it->second->EnumIndex;
            }
        }
    }

    return OrigGetProtoEnumValue(self, protoIdPtr, prototypeClassId);
}

static bool SafeCopyBytes(void* dst, const void* src, size_t n);

static void* GetOrBuildNameEntry(uint64_t id)
{
    auto itEntry = g_NameEntries.find(id);
    if (itEntry != g_NameEntries.end())
        return itEntry->second;

    if (!g_NameTemplateReady)
        return nullptr;

    auto itText = g_NameTexts.find(id);
    if (itText == g_NameTexts.end())
        return nullptr;

    unsigned char* e = (unsigned char*)calloc(1, sizeof(g_NameTemplate));
    if (!e) return nullptr;
    memcpy(e, g_NameTemplate, sizeof(g_NameTemplate));

    const char* text = itText->second.c_str();
    memcpy(e + 0x50, &text, sizeof(text));
    memset(e + 0x58, 0, 8);
    memset(e + 0x60, 0xFF, 8);
    memcpy(e + 0x70, &id, sizeof(id));

    g_NameEntries[id] = e;
    WriteDiagF("[HL] built entry for forged id 0x%016llX -> \"%s\"  (%p)\r\n",
        (unsigned long long)id, text, e);
    return e;
}

static void* __fastcall MyGetLocaleString(void* locale, uint64_t id, char flag)
{

    if (!g_NameTexts.empty() && g_NameTexts.find(id) != g_NameTexts.end()) {
        void* mine = GetOrBuildNameEntry(id);

        static int s_named = 0;
        if (s_named < 20) {
            ++s_named;
            WriteDiagF("[HL] custom name id=0x%016llX -> %s%s\r\n",
                (unsigned long long)id,
                mine ? "OUR entry   <<< SUBSTITUTED" : "no template yet - passing through",
                (s_named == 20) ? "   (further custom-name logging suppressed)" : "");
        }
        if (mine) return mine;
    }

    void* entry = OrigGetLocaleString(locale, id, flag);

    if (!g_NameTemplateReady && entry && entry != (void*)((unsigned char*)locale + 0x3B0)) {
        if (SafeCopyBytes(g_NameTemplate, entry, sizeof(g_NameTemplate)))
            InterlockedExchange(&g_NameTemplateReady, 1);
    }

    static int s_logged = 0;
    static int s_dumped = 0;
    if (s_logged < 60) {
        ++s_logged;

        void* blank = (void*)((unsigned char*)locale + 0x3B0);
        const bool missed = (entry == blank);

        WriteDiagF("[HL] GetLocaleString id=0x%016llX -> %p%s%s\r\n",
            (unsigned long long)id, entry,
            missed ? "  (MISS - blank entry)" : "",
            (s_logged == 60) ? "   (further HL logging suppressed)" : "");

        if (!missed && entry && s_dumped < 2) {
            ++s_dumped;
            unsigned char buf[0xC0];
            if (SafeCopyBytes(buf, entry, sizeof(buf))) {
                for (int row = 0; row < (int)sizeof(buf); row += 16) {
                    char hex[64], asc[20];
                    for (int i = 0; i < 16; ++i) {
                        sprintf_s(hex + i * 3, 4, "%02X ", buf[row + i]);
                        asc[i] = (buf[row + i] >= 0x20 && buf[row + i] < 0x7F)
                                 ? (char)buf[row + i] : '.';
                    }
                    asc[16] = '\0';
                    WriteDiagF("        [HL] entry +%03X  %s |%s|\r\n", row, hex, asc);
                }

                void* textPtr = nullptr;
                memcpy(&textPtr, buf + 0x50, sizeof(textPtr));
                unsigned char tbuf[0x60];
                if (textPtr && SafeCopyBytes(tbuf, textPtr, sizeof(tbuf))) {
                    WriteDiagF("        [HL] *(+0x50) = %p :\r\n", textPtr);
                    for (int row = 0; row < (int)sizeof(tbuf); row += 16) {
                        char hex[64], asc[20];
                        for (int i = 0; i < 16; ++i) {
                            sprintf_s(hex + i * 3, 4, "%02X ", tbuf[row + i]);
                            asc[i] = (tbuf[row + i] >= 0x20 && tbuf[row + i] < 0x7F)
                                     ? (char)tbuf[row + i] : '.';
                        }
                        asc[16] = '\0';
                        WriteDiagF("        [HL]  text +%03X  %s |%s|\r\n", row, hex, asc);
                    }

                    char nar[0x50]; size_t n = 0;
                    for (; n < sizeof(nar) - 1 && tbuf[n]; ++n) nar[n] = (char)tbuf[n];
                    nar[n] = '\0';
                    WriteDiagF("        [HL]  as ansi   : \"%s\"\r\n", nar);
                    wchar_t wide[0x28]; size_t w = 0;
                    for (; w < 0x27; ++w) {
                        wchar_t c; memcpy(&c, tbuf + w * 2, sizeof(c));
                        if (!c) break;
                        wide[w] = c;
                    }
                    wide[w] = L'\0';
                    WriteDiagF("        [HL]  as utf16  : \"%S\"\r\n", wide);
                }
            }
        }
    }
    return entry;
}

#define VERIFY_STACK_LINE   2008

#define VERIFY_STACK_LINE_2 1998

#pragma intrinsic(_ReturnAddress)

typedef USHORT(WINAPI* CaptureStackBackTrace_t)(ULONG, ULONG, PVOID*, PULONG);
static CaptureStackBackTrace_t g_CaptureStack = nullptr;

static void LogAssertStack(void* immediateCaller,
                           const char* what = "contains the failing lookup")
{
    const uintptr_t base = (uintptr_t)g_Base;

    uintptr_t ret = (uintptr_t)immediateCaller;
    if (base && ret >= base && ret - base < 0x4000000)
        WriteLogF("        [STACK] caller   RVA 0x%llX   <<<< %s\r\n",
            (unsigned long long)(ret - base), what);
    else
        WriteLogF("        [STACK] caller   %p  (outside client image)\r\n", immediateCaller);

    if (!g_CaptureStack) return;

    void* frames[28] = {};
    USHORT n = g_CaptureStack(1, 28, frames, nullptr);
    for (USHORT i = 0; i < n; ++i) {
        uintptr_t a = (uintptr_t)frames[i];
        if (base && a >= base && a - base < 0x4000000)
            WriteLogF("        [STACK] frame %-2u RVA 0x%llX\r\n",
                (unsigned)i, (unsigned long long)(a - base));
        else
            WriteLogF("        [STACK] frame %-2u %p  (outside client image)\r\n",
                (unsigned)i, frames[i]);
    }
}

static void* __fastcall MyVerifyFail(const char* expr, const char* file, uint32_t line,
                                     void* flag, const char* msg, int extra)
{
    static int s_loggedV = 0;
    if (s_loggedV < 60) {
        ++s_loggedV;
        WriteLogF("[VERIFY] %s   (%s:%u)%s\r\n",
            expr ? expr : "(null)", file ? file : "(null)", line,
            (s_loggedV == 60) ? "   (further VERIFY logging suppressed)" : "");
    }

    static int s_loggedStack = 0;
    if ((line == VERIFY_STACK_LINE || line == VERIFY_STACK_LINE_2) && s_loggedStack < 3) {
        ++s_loggedStack;
        WriteLogF("[VSTACK] capturing for DataDirectory.cpp:%u\r\n", line);
        LogAssertStack(_ReturnAddress());
    }

    return OrigVerifyFail(expr, file, line, flag, msg, extra);
}

#define PROTO_RECORD_SIZE   0x40
#define PROTO_PTR_OFFSET    0x30
#define PROTO_STRUCT_SIZE   0x428

#define PROTO_DISPLAYNAME_OFF 0x058

#define PROTO_COSTUMEUNREALCLASS_OFF 0x3D0

#define PROTO_DATAREF_OFF     0x008

static bool SafeCopyBytes(void* dst, const void* src, size_t n)
{
    __try { memcpy(dst, src, n); return true; }
    __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}

static bool SafeReadPtr(const void* at, void** out)
{
    __try { *out = *(void* const*)at; return true; }
    __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}

static bool SafeReadU64(const void* at, uint64_t* out)
{
    __try { *out = *(const uint64_t*)at; return true; }
    __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}

static void* CallLocaleSingletonGuarded(LocaleMgrSingleton_t fn)
{
    __try { return fn(); }
    __except (EXCEPTION_EXECUTE_HANDLER) { return nullptr; }
}

static void* CallGetLocaleStringGuarded(GetLocaleString_t fn, void* locale, uint64_t id)
{
    __try { return fn(locale, id, 0); }
    __except (EXCEPTION_EXECUTE_HANDLER) { return nullptr; }
}

static bool SafeReadAnsi(const void* at, char* out, size_t cap)
{
    __try {
        const char* s = (const char*)at;
        size_t i = 0;
        for (; i + 1 < cap && s[i]; ++i) out[i] = s[i];
        out[i] = '\0';
        return true;
    }
    __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}

static bool ResolveLocaleText(uint64_t id, char* out, size_t cap)
{
    if (!out || cap == 0) return false;
    out[0] = '\0';
    if (!OrigGetLocaleString || !g_Base) return false;

    LocaleMgrSingleton_t getSingleton =
        (LocaleMgrSingleton_t)((uintptr_t)g_Base + RVA_LOCALEMGR_SINGLETON);

    void* singleton = CallLocaleSingletonGuarded(getSingleton);
    if (!singleton) return false;

    uint64_t localeRaw = 0;
    if (!SafeReadU64((const char*)singleton + 0x40, &localeRaw) || !localeRaw)
        return false;
    void* locale = (void*)(uintptr_t)localeRaw;

    void* entry = CallGetLocaleStringGuarded(OrigGetLocaleString, locale, id);
    if (!entry) return false;
    if (entry == (void*)((unsigned char*)locale + 0x3B0)) return false;

    uint64_t textPtr = 0;
    if (!SafeReadU64((const char*)entry + 0x50, &textPtr) || !textPtr)
        return false;

    return SafeReadAnsi((const void*)(uintptr_t)textPtr, out, cap);
}

static bool SafeWriteStdStringSso(void* strBase, const char* src, size_t len)
{
    if (len > 15) return false;
    __try {
        char* base = (char*)strBase;
        if (*(const size_t*)(base + 0x18) > 15) return false;
        for (size_t i = 0; i < len; ++i) base[i] = src[i];
        base[len] = '\0';
        *(size_t*)(base + 0x10) = len;
        *(size_t*)(base + 0x18) = 15;
        return true;
    }
    __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}

static bool SafeReadStdString(const void* strBase, char* out, size_t outCap)
{
    __try {
        const char* base = (const char*)strBase;
        size_t size = *(const size_t*)(base + 0x10);
        size_t cap  = *(const size_t*)(base + 0x18);
        const char* src = (cap <= 15) ? base : *(const char* const*)base;
        if (src == nullptr || size > 4096 || outCap == 0) return false;
        size_t n = (size < outCap - 1) ? size : outCap - 1;
        for (size_t i = 0; i < n; ++i) out[i] = src[i];
        out[n] = '\0';
        return true;
    }
    __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}

static void* BuildPrototypeClone(CostumeMod* mod, void* donorRecord)
{
    void* donorProto = nullptr;
    if (!SafeReadPtr((const char*)donorRecord + PROTO_PTR_OFFSET, &donorProto) || !donorProto) {
        WriteDiagF("[H2] \"%s\": donor record has no Prototype* at +0x%X - using donor record\r\n",
            mod->Name.c_str(), PROTO_PTR_OFFSET);
        return nullptr;
    }

    void* recClone   = VirtualAlloc(nullptr, PROTO_RECORD_SIZE, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    void* protoClone = VirtualAlloc(nullptr, PROTO_STRUCT_SIZE, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    if (!recClone || !protoClone) {
        if (recClone)   VirtualFree(recClone, 0, MEM_RELEASE);
        if (protoClone) VirtualFree(protoClone, 0, MEM_RELEASE);
        WriteLogF("[H2] \"%s\": clone allocation failed\r\n", mod->Name.c_str());
        return nullptr;
    }

    if (!SafeCopyBytes(recClone, donorRecord, PROTO_RECORD_SIZE) ||
        !SafeCopyBytes(protoClone, donorProto, PROTO_STRUCT_SIZE)) {
        VirtualFree(recClone, 0, MEM_RELEASE);
        VirtualFree(protoClone, 0, MEM_RELEASE);
        WriteLogF("[H2] \"%s\": faulted copying the donor prototype - using donor record\r\n",
            mod->Name.c_str());
        return nullptr;
    }

    *(uint64_t*)((char*)protoClone + PROTO_DATAREF_OFF) = mod->CustomID;
    WriteDiagF("[H2]   DataRef +0x%03X = 0x%016llX (custom identity)\r\n",
        PROTO_DATAREF_OFF, (unsigned long long)mod->CustomID);

    for (size_t i = 0; i < mod->ProtoPatches.size(); ++i) {
        uint32_t off = mod->ProtoPatches[i].first;
        uint64_t val = mod->ProtoPatches[i].second;
        *(uint64_t*)((char*)protoClone + off) = val;
        WriteDiagF("[H2]   patch +0x%03X = 0x%016llX\r\n", off, (unsigned long long)val);
    }

    *(void**)((char*)recClone + PROTO_PTR_OFFSET) = protoClone;

    WriteDiagF("[H2] \"%s\": prototype CLONED (record %p -> %p, proto %p -> %p, %zu patch(es))\r\n",
        mod->Name.c_str(), donorRecord, recClone, donorProto, protoClone,
        mod->ProtoPatches.size());
    WriteDiagF("[H2]   x64dbg: memory breakpoint (read) on %p..%p covers the cloned prototype\r\n",
        protoClone, (char*)protoClone + PROTO_STRUCT_SIZE - 1);

    if (g_BandScan) {
    WriteLogF("[H2] \"%s\": icon-band scan of donor prototype %p:\r\n",
        mod->Name.c_str(), donorProto);
    for (uint32_t off = 0; off + 8 <= PROTO_STRUCT_SIZE; off += 8) {
        uint64_t v = 0;
        if (!SafeReadU64((const char*)donorProto + off, &v) || v == 0)
            continue;
        uint32_t band = (uint32_t)((v >> 8) & 0xFF);
        if (band < 0x12 || band > 0x18)
            continue;

        const char* note = "";
        switch (off) {
            case 0x078: note = "  IconPath (Entity) - Social";            break;
            case 0x088: note = "  IconPath sibling (HiRes?) - UNTESTED since Hook 7"; break;
            case 0x358: note = "  StoreIconPath (Item) - shadowed/dead";  break;
            case 0x3D0: note = "  CostumeUnrealClass - NOT an icon";      break;
            case 0x3D8: note = "  FullBodyIconPath";                      break;
            case 0x3E0: note = "  FullBodyIconPathDisabled";              break;
            case 0x3E8: note = "  POISON - NEVER WRITE";                  break;
            case 0x3F0: note = "  PortraitIconPath - character sheet";    break;
            case 0x400: note = "  PartyPortraitIconPath";                 break;
            case 0x408: note = "  StoreIconPath (Costume) - store card";  break;
            case 0x410: note = "  POISON - NEVER WRITE";                  break;
            case 0x418: note = "  GetIconAsset type 1";                   break;
            case 0x420: note = "  GetIconAsset type 5";                   break;
            default:    note = "  <-- UNMAPPED";                          break;
        }
        WriteLogF("           +0x%03X = 0x%016llX%s\r\n",
            off, (unsigned long long)v, note);
    }

    WriteLogF("[H2] \"%s\": locale-band scan of donor prototype %p (name candidates):\r\n",
        mod->Name.c_str(), donorProto);
    for (uint32_t off = 0; off + 8 <= PROTO_STRUCT_SIZE; off += 8) {
        uint64_t v = 0;
        if (!SafeReadU64((const char*)donorProto + off, &v) || v == 0)
            continue;
        uint32_t band = (uint32_t)((v >> 8) & 0xFF);
        if (band < 0x03 || band > 0x08)
            continue;

        char text[128];
        const bool got = ResolveLocaleText(v, text, sizeof(text));
        WriteLogF("           +0x%03X = 0x%016llX  %s%s%s\r\n",
            off, (unsigned long long)v,
            got ? "\"" : "(did not resolve)", got ? text : "", got ? "\"" : "");
    }
    }

    return recClone;
}

static void* RecordOrClone(CostumeMod* mod, void* donorRec)
{

    if (!donorRec || mod->ProtoPatches.empty())
        return donorRec;

    void* clone = mod->ClonedRecord;
    if (!clone) {
        clone = BuildPrototypeClone(mod, donorRec);
        mod->ClonedRecord = clone;
    }
    return clone ? clone : donorRec;
}

static void* __fastcall MyLookupProtoRecord(void* db, void* protoIdPtr, void* extra)
{
    uint64_t id = 0;
    if (protoIdPtr && SafeReadU64(protoIdPtr, &id)) {
        auto it = g_ByCustom.find(id);
        if (it != g_ByCustom.end()) {
            CostumeMod* mod = it->second;
            uint64_t donor = mod->DonorID;
            return RecordOrClone(mod, OrigLookupProto(db, &donor, extra));
        }

        if (!g_ByForgedHotspot.empty()) {
            auto hs = g_ByForgedHotspot.find(id);
            if (hs != g_ByForgedHotspot.end() && hs->second.Stock) {
                if (g_MissileProbe && Hs2FirstTime(id))
                    WriteDiagF("[HS2] forged hotspot 0x%016llX -> stock record 0x%016llX  "
                              "(\"%s\")\r\n",
                              (unsigned long long)id, (unsigned long long)hs->second.Stock,
                              hs->second.Mod ? hs->second.Mod->Name.c_str() : "?");
                uint64_t stock = hs->second.Stock;
                return OrigLookupProto(db, &stock, extra);
            }
        }
    }
    return OrigLookupProto(db, protoIdPtr, extra);
}

#define WEP_SCAN_LO   0x000
#define WEP_SCAN_HI   0x440

#define WEP_SEEN_MAX  16384
static void* volatile g_WepSeen[WEP_SEEN_MAX] = {};
static volatile long  g_WepSeenN = 0;

static volatile long g_WepScanned = 0;
static volatile long g_WepHits    = 0;
static volatile long g_WepCapped  = 0;

static bool WepFirstTime(void* proto)
{
    long cnt = g_WepSeenN;
    if (cnt >= WEP_SEEN_MAX) return false;
    for (long i = 0; i < cnt && i < WEP_SEEN_MAX; ++i)
        if (g_WepSeen[i] == proto) return false;
    long slot = InterlockedIncrement(&g_WepSeenN) - 1;
    if (slot >= WEP_SEEN_MAX) return false;
    g_WepSeen[slot] = proto;
    return true;
}

static void ProbeWorldEntityProtoUnrealClass(uint64_t protoId, void* record)
{
    if (!g_MissileProbe || !record || g_EffectByStockAsset.empty()) return;

    void* proto = nullptr;
    if (!SafeReadPtr((const char*)record + PROTO_PTR_OFFSET, &proto) || !proto) return;

    if (g_WepSeenN >= WEP_SEEN_MAX) {

        if (InterlockedIncrement(&g_WepCapped) == 1)
            WriteLogF("[WEP] distinct-prototype cap (%d) REACHED after %ld scans, %ld hit(s) - "
                      "raise WEP_SEEN_MAX; anything created later was NOT examined@",
                      WEP_SEEN_MAX, (long)g_WepScanned, (long)g_WepHits);
        return;
    }
    if (!WepFirstTime(proto)) return;

    long nth = InterlockedIncrement(&g_WepScanned);
    if (nth == 1)
        WriteLog("[WEP] probe is LIVE - scanning entity-creation prototypes for a configured "
                 "stock effect asset. Silence from here means scanned-but-not-found, not "
                 "never-ran.@");
    if ((nth % 2000) == 0)
        WriteLogF("[WEP] … %ld distinct prototypes scanned, %ld hit(s) so far@",
                  nth, (long)g_WepHits);

    for (unsigned off = WEP_SCAN_LO; off + 8 <= WEP_SCAN_HI; off += 8) {
        uint64_t v = 0;
        if (!SafeReadU64((const char*)proto + off, &v) || !v) continue;
        auto hit = g_EffectByStockAsset.find(v);
        if (hit == g_EffectByStockAsset.end()) continue;
        InterlockedIncrement(&g_WepHits);
        WriteLogF("[WEP] protoId 0x%016llX  proto=%p  +0x%03X = 0x%016llX  (\"%s\" effect "
                  "\"%s\")   <-- UnrealClass candidate\r\n",
                  (unsigned long long)protoId, proto, off, (unsigned long long)v,
                  hit->second.Mod ? hit->second.Mod->Name.c_str() : "?",
                  (hit->second.Fx && !hit->second.Fx->Package.empty())
                      ? hit->second.Fx->Package.c_str() : "?");
    }
}

static thread_local CostumeMod* t_ForgedHotspotMod = nullptr;

static const unsigned HS_FORGED_MAX_AGE = 64;
static thread_local unsigned t_ForgedHotspotAge = 0;

static thread_local CostumeMod* t_PendingMissileMod = nullptr;
static thread_local unsigned t_PendingMissileAge = 0;
static const unsigned MP_PENDING_MAX_AGE = 64;

static void* __fastcall MyGetProtoDataRefRecord(void* db, uint64_t* protoIdPtr)
{
    uint64_t id = 0;
    if (protoIdPtr && SafeReadU64(protoIdPtr, &id)) {
        auto it = g_ByCustom.find(id);
        if (it != g_ByCustom.end()) {
            CostumeMod* mod = it->second;
            uint64_t donor = mod->DonorID;
            void* rec = OrigGetProtoDataRefRec(db, &donor);

            void* out = RecordOrClone(mod, rec);

            static int s_logged7 = 0;
            if (s_logged7 < 20) {
                ++s_logged7;
                WriteDiagF("[H7] \"%s\" custom 0x%016llX -> %s %p%s\r\n",
                    mod->Name.c_str(), (unsigned long long)id,
                    (out != rec) ? "CLONE record" : "donor record", out,
                    (s_logged7 == 20) ? "   (further H7 logging suppressed)" : "");
            }
            return out;
        }
    }

    if (!g_ByForgedHotspot.empty()) {
        if (id) {
            auto hs = g_ByForgedHotspot.find(id);
            if (hs != g_ByForgedHotspot.end()) {
                t_ForgedHotspotMod = hs->second.Mod;
                t_ForgedHotspotAge = 0;

                if (g_MissileProbe && Hs7FirstTime(id))
                    WriteDiagF("[HS7] forged hotspot 0x%016llX -> \"%s\"  (record from stock "
                              "0x%016llX)\r\n",
                              (unsigned long long)id,
                              hs->second.Mod ? hs->second.Mod->Name.c_str() : "?",
                              (unsigned long long)hs->second.Stock);

                if (hs->second.Stock) {
                    uint64_t stock = hs->second.Stock;
                    return OrigGetProtoDataRefRec(db, &stock);
                }
            }
            else if (t_ForgedHotspotMod && ++t_ForgedHotspotAge > HS_FORGED_MAX_AGE) {
                if (g_MissileProbe)
                    WriteLogF("[HS7] pending \"%s\" EXPIRED after %u lookups with no class "
                              "resolve - dropped rather than handed to the next entity\r\n",
                              t_ForgedHotspotMod->Name.c_str(), t_ForgedHotspotAge);
                t_ForgedHotspotMod = nullptr;
                t_ForgedHotspotAge = 0;
            }
        }
    }
    return OrigGetProtoDataRefRec(db, protoIdPtr);
}

static uint32_t IconTypeToOffset(uint32_t iconType)
{
    switch (iconType) {
        case 1:  return 0x400;
        case 3:  return 0x3F0;
        default: return 0;
    }
}

static void* __fastcall MyGetIconPath(void* protoThis, void* outString, uint32_t iconType)
{

    if (!g_IconUiAlive) InterlockedExchange(&g_IconUiAlive, 1);

    const uint32_t off = IconTypeToOffset(iconType);
    uint64_t assetId = 0;
    const bool haveAsset = off != 0 && SafeReadU64((const char*)protoThis + off, &assetId);

    void* ret = OrigGetIconPath(protoThis, outString, iconType);

    bool substituted = false;
    if (haveAsset && !g_IconPaths.empty()) {
        std::map<uint64_t, std::string>::iterator ip = g_IconPaths.find(assetId);
        if (ip != g_IconPaths.end())
            substituted = SafeWriteStdStringSso((char*)outString + 0x8,
                                                ip->second.c_str(), ip->second.size());
    }

    static int s_loggedClone = 0;
    static int s_loggedOther = 0;
    if (s_loggedClone >= 40 && s_loggedOther >= 8)
        return ret;

    const char* which = nullptr;
    for (std::map<uint64_t, CostumeMod*>::iterator it = g_ByCustom.begin();
         it != g_ByCustom.end(); ++it) {
        void* rec = it->second->ClonedRecord;
        if (!rec) continue;
        void* proto = nullptr;
        if (SafeReadPtr((const char*)rec + PROTO_PTR_OFFSET, &proto) && proto == protoThis) {
            which = it->second->Name.c_str();
            break;
        }
    }

    const bool isClone = (which != nullptr);
    if (isClone ? (s_loggedClone >= 40) : (s_loggedOther >= 8))
        return ret;
    if (isClone) ++s_loggedClone; else ++s_loggedOther;

    char path[160];
    path[0] = '\0';
    const bool havePath = SafeReadStdString((const char*)outString + 0x8, path, sizeof(path));

    WriteDiagF("[HI] GetIconPath proto=%p  type=%u (+0x%03X)  asset=%s0x%016llX  (%s)  -> \"%s\"%s\r\n",
        protoThis, iconType, off,
        haveAsset ? "" : "unreadable ",
        (unsigned long long)assetId,
        isClone ? which : "not-a-clone",
        havePath ? path : "(unreadable)",
        substituted ? "   <<< SUBSTITUTED" : "");

    return ret;
}

static DWORD g_lastExCode = 0;
static void* g_lastExAddr = nullptr;
static ULONG_PTR g_lastExFaultAddr = 0;
static int CaptureExceptionFilter(EXCEPTION_POINTERS* ep)
{
    g_lastExCode = ep->ExceptionRecord->ExceptionCode;
    g_lastExAddr = ep->ExceptionRecord->ExceptionAddress;
    g_lastExFaultAddr =
        ep->ExceptionRecord->NumberParameters >= 2
        ? ep->ExceptionRecord->ExceptionInformation[1] : 0;
    return EXCEPTION_EXECUTE_HANDLER;
}

static char CallOrigLoaderFName(void* self, int idx, uint64_t fname, char flag, bool* threw)
{
    char r = 0;
    *threw = false;
    int loaderFlag = (flag == 0) ? 1 : 0;
    __try {
        if (pLoader)
            r = pLoader(self, idx, (void*)fname, (char)loaderFlag, nullptr, nullptr);
    }
    __except (CaptureExceptionFilter(GetExceptionInformation())) {
        r = -1; *threw = true;
    }
    return r;
}

static void* FindClassGuarded(void* uclass, const wchar_t* const* cands, int n, int* outForm)
{
    void* found = nullptr;
    *outForm = -1;
    __try {
        for (int i = 0; i < n; ++i) {
            if (!cands[i]) continue;
            found = pStaticFindObject(uclass, nullptr, cands[i], 0);
            if (found) { *outForm = i; break; }
        }
    }
    __except (EXCEPTION_EXECUTE_HANDLER) { found = nullptr; }
    return found;
}

static int BuildClassCandidates(CostumeMod* mod,
    std::wstring& leaf, std::wstring& leafLower, std::wstring& pkgQualified,
    const wchar_t* out[5])
{
    leaf = mod->ClassPath;
    size_t dot = leaf.find_last_of(L'.');
    if (dot != std::wstring::npos) leaf = leaf.substr(dot + 1);
    leafLower = leaf;
    for (auto& ch : leafLower) ch = towlower(ch);

    pkgQualified.clear();
    if (!mod->Chain.empty()) {
        std::string pkg = mod->Chain[0];
        std::wstring wpkg(pkg.begin(), pkg.end());
        pkgQualified = wpkg + L"." + leafLower;
    }

    out[0] = mod->ClassPath.c_str();
    out[1] = (!mod->ClassPathLower.empty() && mod->ClassPathLower != mod->ClassPath)
        ? mod->ClassPathLower.c_str() : nullptr;
    out[2] = pkgQualified.empty() ? nullptr : pkgQualified.c_str();
    out[3] = leaf.c_str();
    out[4] = (leafLower != leaf) ? leafLower.c_str() : nullptr;
    return 5;
}

typedef uint64_t* (__fastcall* AddDataRef_t)(void*, uint64_t*, uint64_t, void*);

#define STRINGT_SIZE 0x48

static bool BuildStringT(unsigned char* obj, const char* s, size_t len)
{
    if (len > 15) return false;
    __try {
        memset(obj, 0, STRINGT_SIZE);
        *(void**)(obj + 0x00) = (void*)(g_Base + RVA_STRINGT_ALLOCATOR);
        memcpy(obj + 0x08, s, len);
        obj[0x08 + len] = 0;
        *(size_t*)(obj + 0x18) = len;
        *(size_t*)(obj + 0x20) = 15;
        return true;
    }
    __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}

static bool CallAddDataRefGuarded(AddDataRef_t fn, void* mgr, uint64_t id, void* name, uint64_t* outVal)
{
    __try {
        uint64_t out = 0;
        fn(mgr, &out, id, name);
        *outVal = out;
        return true;
    }
    __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}

static void RegisterIconAssetIdsOnce()
{
    static bool s_done = false;
    if (s_done || g_IconPaths.empty()) return;
    s_done = true;

    void* cache = nullptr;
    if (!SafeReadPtr(g_Base + RVA_ASSETCACHE_GLOBAL, &cache) || !cache) {
        WriteLogF("    [REG] ClientAssetCache global is null - skipping AssetId registration\r\n");
        return;
    }
    void* mgr = (unsigned char*)cache + ASSETCACHE_MGR_OFFSET;
    AddDataRef_t fn = (AddDataRef_t)(g_Base + RVA_ADDDATAREF);

    WriteLogF("    [REG] registering %zu icon AssetId(s)  cache=%p mgr=%p\r\n",
        g_IconPaths.size(), cache, mgr);

    for (std::map<uint64_t, std::string>::iterator it = g_IconPaths.begin();
         it != g_IconPaths.end(); ++it) {

        static unsigned char s_name[STRINGT_SIZE];
        if (!BuildStringT(s_name, it->second.c_str(), it->second.size())) {
            WriteLogF("    [REG]   0x%016llX \"%s\" - could not build StringT, SKIPPED\r\n",
                (unsigned long long)it->first, it->second.c_str());
            continue;
        }

        uint64_t outVal = 0;
        bool ok = CallAddDataRefGuarded(fn, mgr, it->first, s_name, &outVal);
        WriteDiagF("    [REG]   0x%016llX -> \"%s\"  %s (out=0x%016llX)\r\n",
            (unsigned long long)it->first, it->second.c_str(),
            ok ? "registered" : "*** FAULTED - falling back to Hook I substitution ***",
            (unsigned long long)outVal);
        if (!ok) return;
    }
}

static void LoadIconPackagesOnce(void* self, int idx, char flag)
{
    if (g_IconPackages.empty()) return;
    if (InterlockedCompareExchange(&g_IconPackagesLoaded, 1, 0) != 0) return;

    WriteLogF("    [ICO] loading %zu custom icon package(s)\r\n", g_IconPackages.size());

    for (size_t i = 0; i < g_IconPackages.size(); ++i) {
        int before = ReadNumNames();
        uint64_t fn = MakeFName(g_IconPackages[i].c_str(), FNAME_Add);
        int after = ReadNumNames();
        g_IconPackageFNames.push_back(fn);

        if (g_LoadedPackages.find(fn) != g_LoadedPackages.end()) {
            WriteDiagF("    [ICO]   \"%s\" -> already loaded, skip\r\n", g_IconPackages[i].c_str());
            continue;
        }

        if (IsQuarantined(g_IconPackages[i])) {
            WriteLogF("    [ICO]   \"%s\" -> QUARANTINED, skip\r\n", g_IconPackages[i].c_str());
            continue;
        }

        bool threw = false;
        WriteArmingSentinel(g_IconPackages[i]);
        char r = CallOrigLoaderFName(self, idx, fn, flag, &threw);
        ClearArmingSentinel();
        if (!threw && r > 0) g_LoadedPackages.insert(fn);

        WriteDiagF("    [ICO]   \"%s\" -> fname 0x%016llX  NumNames %d->%d%s  load=%d%s\r\n",
            g_IconPackages[i].c_str(), (unsigned long long)fn, before, after,
            (after > before) ? " (NEW)" : " (existed)", (int)r,
            threw ? "  *** FAULTED ***" : "");
    }

    RegisterIconAssetIdsOnce();
}

static void* ResolveEffectClass(const std::string& classPath, int* outForm)
{
    std::wstring wclass(classPath.begin(), classPath.end());
    std::wstring leaf = wclass;
    size_t dot = leaf.find_last_of(L'.');
    if (dot != std::wstring::npos) leaf = leaf.substr(dot + 1);
    std::wstring wclassLower = wclass, leafLower = leaf;
    for (auto& ch : wclassLower) ch = towlower(ch);
    for (auto& ch : leafLower)   ch = towlower(ch);
    const wchar_t* cands[5] = {
        wclass.c_str(),
        (wclassLower != wclass) ? wclassLower.c_str() : nullptr,
        nullptr,
        leaf.c_str(),
        (leafLower != leaf) ? leafLower.c_str() : nullptr
    };
    return FindClassGuarded(nullptr, cands, 5, outForm);
}

static void ResolveResidentEffectClasses(CostumeMod* mod)
{
    if (!mod || mod->Effects.empty() || !pStaticFindObject) return;

    int resolved = 0, missing = 0, alreadyHad = 0;

    for (auto& kv : mod->Effects) {
        EffectRedirect& e = kv.second;
        if (e.CachedClass) { ++alreadyHad; continue; }
        if (e.Package.empty() || e.Class.empty()) continue;

        uint64_t fn = MakeFName(e.Package.c_str(), FNAME_Find);
        if (!fn || g_LoadedPackages.find(fn) == g_LoadedPackages.end()) continue;

        int form = -1;
        void* found = ResolveEffectClass(e.Class, &form);
        if (found) { e.CachedClass = found; ++resolved; }
        else       { ++missing; }
    }

    if (resolved || missing)
        WriteDiagF("    [FX] arm-time resolve for \"%s\": %d class(es) resolved from "
            "already-resident packages, %d still missing, %d already cached\r\n",
            mod->Name.c_str(), resolved, missing, alreadyHad);
}

static void __fastcall MyPackageLoop(void* self, uint64_t* assetIdPtr, int idx, char flag)
{
    CostumeMod* mod = g_ActiveMod;

    if (!mod || t_InLoop) {
        const bool outer = (t_InLoop == 0);
        OrigLoop(self, assetIdPtr, idx, flag);

        if (outer && idx == 0 && g_IconUiAlive) {
            t_InLoop++;
            LoadIconPackagesOnce(self, idx, flag);
            t_InLoop--;
        }
        return;
    }

    t_InLoop++;
    OrigLoop(self, assetIdPtr, idx, flag);
    t_InLoop--;

    if (idx != 0) return;

    {
        bool anyNew = false;
        for (size_t i = 0; i < mod->ChainFNames.size(); ++i)
            if (g_LoadedPackages.find(mod->ChainFNames[i]) == g_LoadedPackages.end()) { anyNew = true; break; }
        if (!anyNew) {

            ResolveResidentEffectClasses(mod);
            return;
        }
    }

    WriteLogF("    [B2] loading chain of %zu  (idx=0)\r\n", mod->Chain.size());

    WriteArmingSentinel(mod->Name);

    for (size_t i = 0; i < mod->Chain.size(); ++i) {

        uint64_t pkgFn = (i < mod->ChainFNames.size()) ? mod->ChainFNames[i] : 0;
        if (pkgFn && g_LoadedPackages.find(pkgFn) != g_LoadedPackages.end()) {
            WriteDiagF("    [B2]   [%zu] \"%s\" -> already loaded, skip\r\n",
                i, mod->Chain[i].c_str());
            continue;
        }

        bool threw = false;

        char r = CallOrigLoaderFName(self, idx, pkgFn, flag, &threw);

        if (threw) {

            uintptr_t rva = (uintptr_t)g_lastExAddr - (uintptr_t)g_Base;
            WriteLogF("    [B2]   [%zu] \"%s\" -> EXCEPTION 0x%08lX at "
                "base+0x%llX (fault data addr 0x%llX)\r\n",
                i, mod->Chain[i].c_str(),
                (unsigned long)g_lastExCode,
                (unsigned long long)rva,
                (unsigned long long)g_lastExFaultAddr);
        }
        else {

            if (pkgFn) g_LoadedPackages.insert(pkgFn);
            WriteDiagF("    [B2]   [%zu] \"%s\" -> %d%s  (marked loaded)\r\n",
                i, mod->Chain[i].c_str(), (int)r,
                (r == 1) ? " (RESIDENT)" : (r == -1) ? " (REJECTED)" : "");
        }

    }

    ClearArmingSentinel();
    WriteLog("    [B2] chain complete\r\n");

    ResolveResidentEffectClasses(mod);
}

static void* __fastcall MyFindPkgInfo(void* map, uint64_t key)
{

#define H4_ALIAS 0

    CostumeMod* mod = g_ActiveMod;
    bool ours = (mod && mod->Interned && !mod->ChainFNames.empty() &&
        key == mod->ChainFNames[0]);

    void* hit = OrigFindPkgInfo(map, key);

    if (hit && !g_EffectByPkgFName.empty() && pStaticFindObject) {
        auto fx = g_EffectByPkgFName.find(key);

        if (fx != g_EffectByPkgFName.end()) {
            const std::string* lastClass = nullptr;
            void* lastFound = nullptr;
            int   filled = 0, form = -1;

            for (size_t k = 0; k < fx->second.size(); ++k) {
                EffectRedirect* e = fx->second[k];
                if (!e || e->CachedClass || e->Class.empty()) continue;

                void* found;
                if (lastClass && *lastClass == e->Class) {
                    found = lastFound;
                } else {
                    found = ResolveEffectClass(e->Class, &form);
                    lastClass = &e->Class;
                    lastFound = found;
                    if (!found)
                        WriteDiagF("=== [FX] effect package \"%s\" resident but StaticFindObject"
                            "(\"%s\") -> NULL - check the UPK's internal class name "
                            "(`upkrename names`). Stock FX will be used. ===\r\n",
                            e->Package.c_str(), e->Class.c_str());
                }

                if (found) {
                    e->CachedClass = found;
                    ++filled;
                    WriteDiagF("=== [FX] cached effect class \"%s\" via form %d -> %p"
                        "  (entry %zu of %zu backed by this package) ===\r\n",
                        e->Class.c_str(), form, found, k + 1, fx->second.size());
                }
            }
            (void)filled;
        }
    }

#if H4_ALIAS
    if (ours && !hit && mod->DonorPkgFName) {
        void* donor = OrigFindPkgInfo(map, mod->DonorPkgFName);
        if (donor) {
            WriteDiagF("=== [H4] aliased custom 0x%016llX -> donor %p ===\r\n",
                (unsigned long long)key, donor);
            return donor;
        }
    }
#endif

    if (ours) {
        WriteDiagF("=== [H4] custom key 0x%016llX -> %s (passthrough) ===\r\n",
            (unsigned long long)key, hit ? "FOUND" : "null");

#define H4_CACHE_CLASS 1
#if H4_CACHE_CLASS
        if (hit && mod && !mod->CachedClass && pStaticFindObject) {
            std::wstring leaf, leafLower, pkgQ;
            const wchar_t* cands[5];
            BuildClassCandidates(mod, leaf, leafLower, pkgQ, cands);
            int form = -1;
            void* found = FindClassGuarded(nullptr, cands, 5, &form);
            if (found) {
                mod->CachedClass = found;
                WriteDiagF("=== [H4] cached class via form %d -> %p ===\r\n", form, found);
            }
        }
#endif
    }

    return hit;
}

typedef void* (__fastcall* PowerGetUnrealClass_t)(void*, uint64_t*, uint64_t*, uint64_t*);
static PowerGetUnrealClass_t OrigPowerGetUnrealClass = nullptr;

#if PHASE0_POWER_PROBE

static int ProbeVector(const void* vecField, void** elems, int maxElems)
{
    void* vec = nullptr;
    if (!SafeReadPtr(vecField, &vec) || !vec) return 0;

    void* begin = nullptr; void* end = nullptr;
    if (!SafeReadPtr(vec, &begin) || !begin) return 0;
    if (!SafeReadPtr((const char*)vec + 8, &end) || !end) return 0;
    if (end < begin) return 0;
    size_t bytes = (size_t)((const char*)end - (const char*)begin);
    if (bytes > 0x10000) return 0;
    int n = (int)(bytes / 8), got = 0;
    for (int i = 0; i < n && got < maxElems; ++i) {
        void* e = nullptr;
        if (SafeReadPtr((const char*)begin + (size_t)i * 8, &e) && e) elems[got++] = e;
    }
    return got;
}
#endif

static void* __fastcall MyPowerGetUnrealClass(void* proto, uint64_t* out,
                                              uint64_t* keyA, uint64_t* keyB)
{
    void* r = OrigPowerGetUnrealClass(proto, out, keyA, keyB);

    if (!g_ByForgedAsset.empty() && keyB && out) {
        uint64_t costumeKey = 0, resolved = 0;
        if (SafeReadU64(keyB, &costumeKey) && costumeKey &&
            SafeReadU64(out, &resolved) && resolved) {
            auto c = g_ByForgedAsset.find(costumeKey);
            if (c != g_ByForgedAsset.end()) {
                auto fx = c->second->Effects.find(resolved);

                if (fx != c->second->Effects.end()) {
                    if (g_FxDryRun) {

                        static int s_dryLogged = 0;
                        if (s_dryLogged < 40) {
                            ++s_dryLogged;
                            WriteDiagF("[FX] DRY-RUN \"%s\": would redirect 0x%016llX -> "
                                "0x%016llX%s\r\n",
                                c->second->Name.c_str(), (unsigned long long)resolved,
                                (unsigned long long)fx->second.To,
                                (s_dryLogged == 40) ? "   (further FX logging suppressed)" : "");
                        }
                    } else if (fx->second.CachedClass) {

                        *out = fx->second.To;
                    } else {
                        static int s_notReady = 0;
                        if (s_notReady < 10) {
                            ++s_notReady;
                            WriteLogF("[FX] \"%s\": 0x%016llX not substituted - package "
                                "\"%s\" not resident yet, using stock%s\r\n",
                                c->second->Name.c_str(), (unsigned long long)resolved,
                                fx->second.Package.c_str(),
                                (s_notReady == 10) ? "   (further suppressed)" : "");
                        }
                    }
                }
            }
        }
    }

#if PHASE0_POWER_PROBE
    if (!g_PowerProbe || !proto) return r;
    if (g_PowerSeen.size() >= POWERPROBE_MAX) return r;
    if (!g_PowerSeen.insert(proto).second) return r;

    uint64_t a = 0, b = 0, res = 0, def = 0;
    SafeReadU64(keyA, &a);
    SafeReadU64(keyB, &b);
    SafeReadU64(out,  &res);
    SafeReadU64((const char*)proto + 0x2D0, &def);

    WriteLogF("[POWER] #%03llu proto=%p  keyA=0x%016llX keyB=0x%016llX  "
        "default=0x%016llX -> out=0x%016llX%s\r\n",
        (unsigned long long)g_PowerSeen.size(), proto,
        (unsigned long long)a, (unsigned long long)b,
        (unsigned long long)def, (unsigned long long)res,
        (res && def && res != def) ? "   <<< OVERRIDE FIRED" : "");

    void* ovrs[16];
    int n = ProbeVector((const char*)proto + 0x1F8, ovrs, 16);
    if (n > 0) {
        WriteLogF("        overrideProto entries: %d\r\n", n);
        for (int i = 0; i < n; ++i) {
            uint64_t k = 0, asset = 0;
            SafeReadU64((const char*)ovrs[i] + 0x68, &k);
            SafeReadU64((const char*)ovrs[i] + 0x70, &asset);
            void* reps[16];
            int m = ProbeVector((const char*)ovrs[i] + 0x60, reps, 16);
            WriteLogF("          [%d] key=0x%016llX -> asset=0x%016llX  (%d replacement(s))\r\n",
                i, (unsigned long long)k, (unsigned long long)asset, m);
            for (int j = 0; j < m; ++j) {
                uint64_t rk = 0, ra = 0;
                SafeReadU64((const char*)reps[j] + 0x60, &rk);
                SafeReadU64((const char*)reps[j] + 0x68, &ra);
                WriteLogF("              repl[%d] key=0x%016llX -> asset=0x%016llX\r\n",
                    j, (unsigned long long)rk, (unsigned long long)ra);
            }
        }
    }

    if (g_PowerSeen.size() == POWERPROBE_MAX)
        WriteLog("[POWER] limit reached - further powers suppressed.\r\n");
#endif
    return r;
}

typedef void* (__fastcall* CondGetUnrealClass_t)(void*, uint64_t*, uint64_t*, uint64_t);
static CondGetUnrealClass_t OrigCondGetUnrealClass = nullptr;

#if PHASE0_POWER_PROBE
static std::set<void*> g_CondSeen;
#endif

static void* __fastcall MyCondGetUnrealClass(void* proto, uint64_t* out,
                                             uint64_t* keyA, uint64_t arg4)
{
    void* r = OrigCondGetUnrealClass(proto, out, keyA, arg4);

    if (!g_ByForgedAsset.empty() && keyA && out) {
        uint64_t costumeKey = 0, resolved = 0;
        if (SafeReadU64(keyA, &costumeKey) && costumeKey &&
            SafeReadU64(out, &resolved) && resolved) {
            auto c = g_ByForgedAsset.find(costumeKey);

            if (c == g_ByForgedAsset.end() && g_FxHeroKeyConditions && g_ActiveMod)
                c = g_ByForgedAsset.find(g_ActiveMod->ForgedAsset);

            if (c != g_ByForgedAsset.end()) {
                auto fx = c->second->Effects.find(resolved);
                if (fx != c->second->Effects.end()) {
                    if (g_FxDryRun) {
                        static int s_condDry = 0;
                        if (s_condDry < 40) {
                            ++s_condDry;
                            WriteDiagF("[FXC] DRY-RUN \"%s\": would redirect 0x%016llX -> "
                                "0x%016llX%s\r\n",
                                c->second->Name.c_str(), (unsigned long long)resolved,
                                (unsigned long long)fx->second.To,
                                (s_condDry == 40) ? "   (further FXC logging suppressed)" : "");
                        }
                    } else if (fx->second.CachedClass) {
                        *out = fx->second.To;
                    }
                }
            }
        }
    }

#if PHASE0_POWER_PROBE

    if (g_PowerProbe && proto && g_CondSeen.size() < POWERPROBE_MAX &&
        g_CondSeen.insert(proto).second) {
        uint64_t a = 0, res = 0;
        SafeReadU64(keyA, &a);
        SafeReadU64(out, &res);
        const char* who = "";
        if (a && g_ByForgedAsset.find(a) != g_ByForgedAsset.end()) who = "   <<< FORGED COSTUME KEY";
        WriteLogF("[COND] #%03d proto=%p  keyA=0x%016llX arg4=0x%llX  out=0x%016llX%s\r\n",
                  (int)g_CondSeen.size(), proto, (unsigned long long)a,
                  (unsigned long long)arg4, (unsigned long long)res, who);
    }
#endif
    return r;
}

typedef void* (__fastcall* CreateMissile_t)(void*, void*, void*, void*, void*, void*);
static CreateMissile_t OrigCreateMissile = nullptr;

#if PHASE0_POWER_PROBE
static int g_MissileLogged = 0;

static void* volatile g_OwnerScanned[8] = {};
static volatile long  g_OwnerScannedN = 0;

static bool OwnerScanOnce(void* owner)
{
    long cnt = g_OwnerScannedN;
    if (cnt >= 8) return false;
    for (long i = 0; i < cnt && i < 8; ++i)
        if (g_OwnerScanned[i] == owner) return false;
    long slot = InterlockedIncrement(&g_OwnerScannedN) - 1;
    if (slot >= 8) return false;
    g_OwnerScanned[slot] = owner;
    return true;
}
#endif

typedef void* (__fastcall* HotspotGetClass_t)(void*, void*, void*, void*);
static HotspotGetClass_t OrigHotspotGetClass = nullptr;

typedef void (__fastcall* SetAuthTicket_t)(void*, void*, void*, void*);
static SetAuthTicket_t OrigSetAuthTicket = nullptr;

static volatile long g_AuthTicketSeen = 0;

static unsigned __int64 g_SessionId = 0;

static void __fastcall MySetAuthTicket(void* loginMgr, void* incoming, void* p3, void* p4)
{

    if (OrigSetAuthTicket) OrigSetAuthTicket(loginMgr, incoming, p3, p4);

    long n = InterlockedIncrement(&g_AuthTicketSeen);
    if (n > 4) return;

    if (!loginMgr)
    {
        WriteLog("[AT] SetAuthTicket fired with a NULL LoginManager - nothing to read\n");
        return;
    }

    void* ticket = *(void**)((unsigned char*)loginMgr + LOGINMGR_AUTHTICKET_OFF);

    if (!ticket)
    {
        WriteLogF("[AT] #%ld  m_authTicket at +0x%X is NULL - the original took its VerifyFail "
                  "path, so there is nothing stored yet", n, (unsigned)LOGINMGR_AUTHTICKET_OFF);
        return;
    }

    WriteLogF("[AT] #%ld  LoginManager=%p  m_authTicket(+0x%X)=%p", n, loginMgr,
              (unsigned)LOGINMGR_AUTHTICKET_OFF, ticket);

    unsigned __int64 sessionId = *(unsigned __int64*)((unsigned char*)ticket + AUTHTICKET_SESSIONID_OFF);

    if (sessionId == 0)
        WriteLog("[AT] sessionId is ZERO - a FAILED login ticket. Not stored.\n");
    else
        g_SessionId = sessionId;

    unsigned __int64 predicted = sessionId;
    WriteLogF("[AT]     sessionId (+0x%02X) = 0x%016llX\n",
              (unsigned)AUTHTICKET_SESSIONID_OFF, predicted);

    WriteLog("[AT]     scan of the ticket (non-zero u64s, offset = value):\n");
    int shown = 0;
    for (unsigned off = 0; off + 8 <= 0x80; off += 8)
    {
        unsigned __int64 v = *(unsigned __int64*)((unsigned char*)ticket + off);
        if (v == 0) continue;
        WriteLogF("[AT]       +0x%02X = 0x%016llX%s", off, v,
                  (off == AUTHTICKET_SESSIONID_OFF) ? "   <-- sessionId" : "");
        ++shown;
    }
    if (shown == 0)
        WriteLog("[AT]     ...every u64 in +0x00..+0x80 was ZERO - the ticket is not where "
                 "we think, or has not been populated yet");
}

typedef void* (__fastcall* ProjectileGetClass_t)(void*, void*, void*, void*);
static ProjectileGetClass_t OrigProjectileGetClass = nullptr;

#define MP_SEEN_MAX 32
static CostumeMod* g_MpSeen[MP_SEEN_MAX];
static volatile long g_MpSeenN = 0;

static bool MpFirstTime(CostumeMod* m)
{
    long n = g_MpSeenN;
    if (n > MP_SEEN_MAX) n = MP_SEEN_MAX;
    for (long i = 0; i < n; ++i)
        if (g_MpSeen[i] == m) return false;
    long slot = InterlockedIncrement(&g_MpSeenN) - 1;
    if (slot >= MP_SEEN_MAX) return false;
    g_MpSeen[slot] = m;
    return true;
}

static CostumeMod* HotspotOwnerCostume(void* we, unsigned* foundOff, bool* viaPointer)
{
    if (!we || g_ByForgedAsset.empty()) return nullptr;

    {
        uint64_t c = 0;
        if (SafeReadU64((const char*)we + OWNER_COSTUME_OFF, &c) && c) {
            auto self = g_ByForgedAsset.find(c);
            if (self != g_ByForgedAsset.end()) {
                if (foundOff)   *foundOff = OWNER_COSTUME_OFF;
                if (viaPointer) *viaPointer = false;
                return self->second;
            }
        }
    }

    if (g_MissileProbe) {
        for (unsigned off = HS_SCAN_LO; off + 8 <= HS_SCAN_HI; off += 8) {
            uint64_t v = 0;
            if (!SafeReadU64((const char*)we + off, &v) || !v) continue;

            auto direct = g_ByForgedAsset.find(v);
            if (direct != g_ByForgedAsset.end()) {
                static int s_stray = 0;
                if (s_stray < 8) {
                    ++s_stray;
                    WriteLogF("[HS]     stray ForgedAsset 0x%016llX (%s) at +0x%03X "
                              "- IGNORED, this is not a creator field\r\n",
                              (unsigned long long)v, direct->second->Name.c_str(), off);
                }
            }
        }
    }

    return nullptr;
}

#define HS_SEEN_MAX 64
static void* volatile g_HsSeen[HS_SEEN_MAX] = {};
static volatile long  g_HsSeenN = 0;

static bool HsFirstTime(void* we)
{
    long cnt = g_HsSeenN;
    if (cnt >= HS_SEEN_MAX) return false;
    for (long i = 0; i < cnt && i < HS_SEEN_MAX; ++i)
        if (g_HsSeen[i] == we) return false;
    long slot = InterlockedIncrement(&g_HsSeenN) - 1;
    if (slot >= HS_SEEN_MAX) return false;
    g_HsSeen[slot] = we;
    return true;
}

static void* __fastcall MyHotspotGetClass(void* p1, void* p2, void* p3, void* p4)
{
    void* we = nullptr;
    if (p1) SafeReadPtr((const char*)p1 + HS_WORLDENTITY_OFF, &we);

    if (we) {
        uint64_t c = 0;
        if (SafeReadU64((const char*)we + OWNER_COSTUME_OFF, &c) && c &&
            g_ByForgedAsset.find(c) != g_ByForgedAsset.end())
            g_LocalAvatar = we;
    }

    unsigned off = 0;
    bool viaPtr = false;

    CostumeMod* fromServer = t_ForgedHotspotMod;
    const unsigned serverAge = t_ForgedHotspotAge;
    CostumeMod* mine = fromServer ? fromServer : HotspotOwnerCostume(we, &off, &viaPtr);

    if (g_MissileProbe && we && HsFirstTime(we)) {
        {
            WriteLogF("[HS] worldEntity=%p  armed=%s  owner=%s%s\r\n",
                      we, g_ActiveMod ? g_ActiveMod->Name.c_str() : "(none)",
                      mine ? mine->Name.c_str() : "(not one of ours)",
                      mine ? (fromServer ? "  (SERVER forged prototype ref)"
                                         : (viaPtr ? "  (via avatar pointer)"
                                                   : "  (ForgedAsset inline)"))
                           : "");
            if (mine && fromServer)

                WriteLogF("[HS]     server said so %u lookup(s) ago\r\n", serverAge);
            else if (mine)
                WriteLogF("[HS]     matched at worldEntity+0x%03X%s\r\n", off,
                          (off == OWNER_COSTUME_OFF)
                              ? "  (the proven avatar costume field - this entity IS an avatar)"
                              : "  (CORRELATION - unvalidated)");
            if (mine && !g_HotspotFx)
                WriteLog("[HS]     window NOT opened - \"hotspotFx\": false; hotspots render stock\r\n");
            if (!mine && !g_ByForgedHotspot.empty())

                WriteLogF("[HS]     no forged ref for this entity (%zu forged hotspot(s) "
                          "configured)\r\n", g_ByForgedHotspot.size());
        }
    }

    CostumeMod* prev = t_HotspotMod;
    t_HotspotMod = g_HotspotFx ? mine : nullptr;
    void* r = nullptr;
    if (OrigHotspotGetClass) r = OrigHotspotGetClass(p1, p2, p3, p4);
    t_HotspotMod = prev;

    t_ForgedHotspotMod = nullptr;
    t_ForgedHotspotAge = 0;
    return r;
}

static void* __fastcall MyProjectileGetClass(void* p1, void* p2, void* p3, void* p4)
{

    CostumeMod* fromServer = t_ForgedHotspotMod;
    const unsigned serverAge = t_ForgedHotspotAge;

    CostumeMod* fromLocal = nullptr;
    unsigned localAge = 0;
    if (!fromServer && t_PendingMissileMod) {
        if (++t_PendingMissileAge <= MP_PENDING_MAX_AGE) {
            fromLocal = t_PendingMissileMod;
            localAge = t_PendingMissileAge;
        } else {
            t_PendingMissileMod = nullptr;
            t_PendingMissileAge = 0;
        }
    }
    CostumeMod* owner = fromServer ? fromServer : fromLocal;

    if (g_MissileProbe && MpFirstTime(owner)) {
        if (fromServer) {
            WriteLogF("[MP] projectile class resolve  owner=%s  (SERVER forged prototype ref, "
                      "%u lookup(s) ago)\r\n", fromServer->Name.c_str(), serverAge);
            if (!g_HotspotFx)
                WriteLog("[MP]     window NOT opened - \"hotspotFx\": false; "
                         "missiles render stock\r\n");
        } else if (fromLocal) {

            WriteLogF("[MP] projectile class resolve  owner=%s  (LOCAL createMissile, "
                      "%u lookup(s) ago - client-predicted missile)\r\n",
                      fromLocal->Name.c_str(), localAge);
            if (!g_HotspotFx)
                WriteLog("[MP]     window NOT opened - \"hotspotFx\": false; "
                         "missiles render stock\r\n");
        } else if (!g_ByForgedHotspot.empty()) {
            WriteLogF("[MP] projectile class resolve  owner=(none) - no forged ref for this "
                      "entity (%zu forged prototype(s) configured)\r\n",
                      g_ByForgedHotspot.size());
        }
    }

    CostumeMod* prev = t_HotspotMod;
    t_HotspotMod = g_HotspotFx ? owner : nullptr;
    void* r = nullptr;
    if (OrigProjectileGetClass) r = OrigProjectileGetClass(p1, p2, p3, p4);
    t_HotspotMod = prev;

    t_ForgedHotspotMod = nullptr;
    t_ForgedHotspotAge = 0;
    t_PendingMissileMod = nullptr;
    t_PendingMissileAge = 0;
    return r;
}

static void* __fastcall MyCreateMissile(void* self, void* a2, void* a3, void* a4,
                                        void* a5, void* a6)
{
    void* owner = nullptr;
    if (self) SafeReadPtr((const char*)self + MP_OWNER_OFF, &owner);

#if PHASE0_POWER_PROBE
    if (g_MissileProbe && g_MissileLogged < 12) {
        ++g_MissileLogged;
        uint64_t lead = 0;
        SafeReadU64((const char*)owner + OWNER_COSTUME_OFF, &lead);
        WriteLogF("[M] #%02d createMissile this=%p  m_owner(+0x18)=%p  [+0x788]=0x%016llX%s\r\n",
                  g_MissileLogged, self, owner, (unsigned long long)lead,
                  (g_MissileLogged == 12) ? "   (further M logging suppressed)" : "");
    }

#if PHASE0_POWER_PROBE

    if (g_BandScan && g_MissileProbe && owner && OwnerScanOnce(owner)) {
        int hits = 0;
        for (unsigned off = OWNER_SCAN_LO; off + 8 <= OWNER_SCAN_HI && hits < 12; off += 8) {
            uint64_t v = 0;
            if (!SafeReadU64((const char*)owner + off, &v) || !v) continue;
            for (auto& kv : g_ByEnum) {
                CostumeMod* m = &kv.second;
                const char* what = nullptr;
                if      (v == m->CustomID)    what = "CustomID";
                else if (v == m->DonorID)     what = "DonorID";
                else if (v == m->ForgedAsset) what = "ForgedAsset";
                else if (v == m->DonorAsset)  what = "DonorAsset";
                else if (m->ClonedRecord && v == (uint64_t)(uintptr_t)m->ClonedRecord)
                    what = "ClonedRecord*";
                if (!what) continue;
                ++hits;
                WriteLogF("[OWN] owner=%p  +0x%03X = 0x%016llX  == \"%s\".%s\r\n",
                          owner, off, (unsigned long long)v, m->Name.c_str(), what);
                break;
            }
        }
        if (!hits)
            WriteLogF("[OWN] owner=%p  scanned +0x%X..+0x%X - NO costume identity found. The "
                      "field is not a raw id in this band (may be a Prototype* or an index) - "
                      "widen the band or dump the lead's target.\r\n",
                      owner, OWNER_SCAN_LO, OWNER_SCAN_HI);
    }
#endif
#endif

    CostumeMod* mine = nullptr;
    if (owner) {
        uint64_t ownerCostume = 0;
        if (SafeReadU64((const char*)owner + OWNER_COSTUME_OFF, &ownerCostume) && ownerCostume) {
            auto c = g_ByForgedAsset.find(ownerCostume);
            if (c != g_ByForgedAsset.end()) mine = c->second;
        }
    }

    t_PendingMissileMod = mine;
    t_PendingMissileAge = 0;

    CostumeMod* prev = t_MissileMod;
    t_MissileMod = mine;
    void* r = nullptr;
    if (OrigCreateMissile) r = OrigCreateMissile(self, a2, a3, a4, a5, a6);
    t_MissileMod = prev;

    t_PendingMissileMod = mine;
    t_PendingMissileAge = 0;
    return r;
}

typedef void (__fastcall* FireProjectile_t)(void*);
static FireProjectile_t OrigFireProjectile = nullptr;

#if PHASE0_POWER_PROBE
static int g_FpLogged = 0;
#endif

static void __fastcall MyFireProjectile(void* self)
{
#if PHASE0_POWER_PROBE

    if (g_MissileProbe && self && g_FpLogged < 24) {
        ++g_FpLogged;
        void* cls = nullptr;
        SafeReadPtr((const char*)self + FP_PROJECTILECLASS_OFF, &cls);

        const char* known = "";
        const char* which = "";
        for (auto& kv : g_ByForgedAsset) {
            for (auto& e : kv.second->Effects) {
                if (e.second.CachedClass && e.second.CachedClass == cls) {
                    known = "   <<< OUR CUSTOM class:";
                    which = e.second.Package.c_str();
                    break;
                }
                if (e.second.StockClass && e.second.StockClass == cls) {
                    known = "   <<< STOCK class of:";
                    which = e.second.Package.c_str();
                    break;
                }
            }
            if (*known) break;
        }
        WriteLogF("[FP] #%02d this=%p  +0x170(class)=%p%s%s%s\r\n",
                  g_FpLogged, self, cls, known, which,
                  (g_FpLogged == 24) ? "   (further FP logging suppressed)" : "");
    }
#endif
    OrigFireProjectile(self);
}

static uint64_t ReadDataRefGuarded(uint64_t* dataRef)
{
    uint64_t id = 0;
    if (dataRef) {
        __try { id = *dataRef; }
        __except (EXCEPTION_EXECUTE_HANDLER) { id = 0; }
    }
    return id;
}

#if PHASE0_POWER_PROBE

#define FXSITE_MAX 48
static uint64_t volatile g_FxSiteSeen[FXSITE_MAX] = {};
static volatile long     g_FxSiteSeenN = 0;
static int               g_FxSiteStacks = 0;

#define FXSITE_SITES 12
static uint64_t volatile g_FxSiteRvas[FXSITE_SITES] = {};
static volatile long     g_FxSiteRvasN = 0;

static bool FxSiteNewCallSite(uint64_t rva)
{
    long cnt = g_FxSiteRvasN;
    if (cnt >= FXSITE_SITES) return false;
    for (long i = 0; i < cnt && i < FXSITE_SITES; ++i)
        if (g_FxSiteRvas[i] == rva) return false;
    long slot = InterlockedIncrement(&g_FxSiteRvasN) - 1;
    if (slot >= FXSITE_SITES) return false;
    g_FxSiteRvas[slot] = rva;
    return true;
}

static bool FxSiteFirstTime(uint64_t id)
{
    long cnt = g_FxSiteSeenN;
    if (cnt >= FXSITE_MAX) return false;
    for (long i = 0; i < cnt && i < FXSITE_MAX; ++i)
        if (g_FxSiteSeen[i] == id) return false;
    long slot = InterlockedIncrement(&g_FxSiteSeenN) - 1;
    if (slot >= FXSITE_MAX) return false;
    g_FxSiteSeen[slot] = id;
    return true;
}

#define FXWIN_MAX 32
static uint64_t volatile g_FxWinSeen[FXWIN_MAX] = {};
static volatile long     g_FxWinSeenN = 0;

static bool FxWindowFirstTime(uint64_t id)
{
    long cnt = g_FxWinSeenN;
    if (cnt >= FXWIN_MAX) return false;
    for (long i = 0; i < cnt && i < FXWIN_MAX; ++i)
        if (g_FxWinSeen[i] == id) return false;
    long slot = InterlockedIncrement(&g_FxWinSeenN) - 1;
    if (slot >= FXWIN_MAX) return false;
    g_FxWinSeen[slot] = id;
    return true;
}

static void ProbeFxSite(uint64_t id, void* caller)
{
    auto s = g_EffectByStockAsset.find(id);
    if (s == g_EffectByStockAsset.end()) return;
    if (!FxSiteFirstTime(id)) return;

    CostumeMod* w = CurrentFxOwner();
    const char* wtag = t_MissileMod ? "M" : (t_HotspotMod ? "MP/HS" : "-");
    bool ready = (s->second.Fx->CachedClass != nullptr);

    const uintptr_t base = (uintptr_t)g_Base;
    uint64_t rva = 0;
    if (base && (uintptr_t)caller >= base && (uintptr_t)caller - base < 0x4000000)
        rva = (uint64_t)((uintptr_t)caller - base);

    WriteLogF("[FXSITE] 0x%016llX (%s) resolved - fxWindow=%s%s via=%s  ourClass=%s  armed=%s  "
              "caller RVA 0x%llX\r\n",
        (unsigned long long)id, s->second.Fx->Package.c_str(),
        w ? "YES" : "no",
        (w && w == s->second.Mod) ? " (OURS)" : (w ? " (a DIFFERENT costume)" : ""),
        wtag,
        ready ? "CACHED" : "not resident yet",
        g_ActiveMod ? g_ActiveMod->Name.c_str() : "(none)",
        (unsigned long long)rva);

    if (!w && ready && g_FxSiteStacks < FXSITE_SITES && FxSiteNewCallSite(rva)) {
        ++g_FxSiteStacks;
        LogAssertStack(caller, "resolved this effect - A CALL SITE NOT SEEN BEFORE");
    }
}
#endif

static void* __fastcall MyGLUC(void* assetCache, uint64_t* dataRef)
{
    uint64_t id = ReadDataRefGuarded(dataRef);

#if PHASE0_POWER_PROBE

    if (!g_FxStackIds.empty() && id && g_FxStackIdHits < 10 &&
        g_FxStackIds.find(id) != g_FxStackIds.end()) {
        ++g_FxStackIdHits;

        WriteLogF("[FXTRACE] #%d  0x%016llX  missileWindow=%s  hotspotWindow=%s  armed=%s\r\n",
                  g_FxStackIdHits, (unsigned long long)id,
                  t_MissileMod ? "YES" : "no",
                  t_HotspotMod ? t_HotspotMod->Name.c_str() : "no",
                  g_ActiveMod ? g_ActiveMod->Name.c_str() : "(none)");
        LogAssertStack(_ReturnAddress(), "resolved the traced id");
    }

    if (g_MissileProbe && id && !g_EffectByStockAsset.empty())
        ProbeFxSite(id, _ReturnAddress());
#endif

    CostumeMod* fxOwner = CurrentFxOwner();
    if (fxOwner && id && !g_FxDryRun) {
        auto fx = fxOwner->Effects.find(id);

        if (fx != fxOwner->Effects.end() && fx->second.CachedClass) {
            static int s_mSub = 0;
            if (s_mSub < 20) {
                ++s_mSub;
                WriteDiagF("[%s] REDIRECT \"%s\" 0x%016llX -> 0x%016llX (%s)%s\r\n",
                          t_MissileMod ? "M" : "HS",
                          fxOwner->Name.c_str(), (unsigned long long)id,
                          (unsigned long long)fx->second.To, fx->second.Package.c_str(),
                          (s_mSub == 20) ? "   (further suppressed)" : "");
            }
            return fx->second.CachedClass;
        }
    }

#if PHASE0_POWER_PROBE

    CostumeMod* probeOwner = CurrentFxOwner();
    if (g_MissileProbe && probeOwner && id && FxWindowFirstTime(id)) {
        bool ours = (probeOwner->Effects.find(id) != probeOwner->Effects.end());
        WriteDiagF("[%s] resolving 0x%016llX inside the FX window (owner wears \"%s\")%s%s\r\n",
                  t_MissileMod ? "M" : "MP/HS",
                  (unsigned long long)id, probeOwner->Name.c_str(),
                  ours ? "   <<< ONE OF OURS" : "   (NOT configured for this costume)",
                  (g_FxWinSeenN >= FXWIN_MAX) ? "   (budget full - further ids unreported)" : "");
    }
#endif

    uint64_t  donorSubst = 0;
    uint64_t* passRef    = dataRef;
    EffectRedirect* fxHit = nullptr;

    if (!g_EffectByForgedAsset.empty() && id) {
        auto e = g_EffectByForgedAsset.find(id);
        if (e != g_EffectByForgedAsset.end() && e->second->From) {
            fxHit      = e->second;
            donorSubst = fxHit->From;
            passRef    = &donorSubst;
        }
    }

    if (!fxHit && !g_ByForgedAsset.empty() && id) {
        auto f = g_ByForgedAsset.find(id);
        if (f != g_ByForgedAsset.end() && f->second->DonorAsset) {
            donorSubst = f->second->DonorAsset;
            passRef    = &donorSubst;
            static int s_substLogged = 0;
            if (s_substLogged < 4) {
                ++s_substLogged;
                WriteDiagF("[H3] \"%s\": forged 0x%016llX -> resolving via donor "
                    "0x%016llX (forged ids are costume KEYS, not loadable assets)\r\n",
                    f->second->Name.c_str(), (unsigned long long)id,
                    (unsigned long long)donorSubst);
            }
        }
    }

    void* cls = OrigGLUC(assetCache, passRef);

#if PHASE0_POWER_PROBE

    if (g_MissileProbe && cls && id && !g_EffectByStockAsset.empty()) {
        auto st = g_EffectByStockAsset.find(id);
        if (st != g_EffectByStockAsset.end() && !st->second.Fx->StockClass)
            st->second.Fx->StockClass = cls;
    }
#endif

    if (fxHit) {
        if (fxHit->CachedClass) {

            if (!fxHit->Answered) {
                fxHit->Answered = true;
                WriteDiagF("[FX] answering 0x%016llX with custom class %p (\"%s\")\r\n",
                    (unsigned long long)id, fxHit->CachedClass, fxHit->Class.c_str());
            }
            return fxHit->CachedClass;
        }
        return cls;
    }

#if PHASE0_ASSET_PROBE

    ProbeAsset(id, cls);
#endif

    CostumeMod* mod = g_ActiveMod;
    if (!mod) return cls;

    const bool matched = mod->ForgedAsset
        ? (id == mod->ForgedAsset || (!g_PerAvatarMesh && id == mod->DonorAsset))
        : (id == mod->DonorAsset);

    if (matched && mod->ForgedAsset) {
        static int s_whichLogged = 0;
        if (s_whichLogged < 4) {
            ++s_whichLogged;
            WriteDiagF("[H3] \"%s\": matched via %s (0x%016llX)%s\r\n", mod->Name.c_str(),
                (id == mod->ForgedAsset) ? "FORGED CostumeUnrealClass" : "donorAsset (clone not live yet)",
                (unsigned long long)id,
                g_PerAvatarMesh ? "   [perAvatarMesh: donor id NOT accepted]" : "");
        }
    }

    if (!matched) {

        if (g_PerAvatarMesh && mod->ForgedAsset && id == mod->DonorAsset) {
            static int s_refused = 0;
            if (s_refused < 6) {
                ++s_refused;
                WriteDiagF("[H3] \"%s\": donor id 0x%016llX REFUSED (perAvatarMesh) - correct "
                    "for another player wearing the stock donor; if your OWN mesh is the donor, "
                    "the prototype clone is not live yet%s\r\n",
                    mod->Name.c_str(), (unsigned long long)id,
                    (s_refused == 6) ? "   (further refusals suppressed)" : "");
            }
            return cls;
        }

        static int s_mismatchLogged = 0;
        if (id != 0 && s_mismatchLogged < 20) {
            ++s_mismatchLogged;
            if (mod->ForgedAsset) {
                WriteDiagF("[H3] armed \"%s\" but AssetId 0x%016llX matches neither "
                    "forged 0x%016llX nor donor 0x%016llX - ignoring%s\r\n",
                    mod->Name.c_str(), (unsigned long long)id,
                    (unsigned long long)mod->ForgedAsset,
                    (unsigned long long)mod->DonorAsset,
                    (s_mismatchLogged == 20) ? "   (further H3 mismatches suppressed)" : "");
            } else {
                WriteDiagF("[H3] armed \"%s\" but AssetId 0x%016llX != donorAsset "
                    "0x%016llX - ignoring%s\r\n",
                    mod->Name.c_str(), (unsigned long long)id,
                    (unsigned long long)mod->DonorAsset,
                    (s_mismatchLogged == 20) ? "   (further H3 mismatches suppressed)" : "");
            }
        }
        return cls;
    }

    void* uclass = *(void**)(g_Base + RVA_UCLASS_STATICCLASS);
    if (!uclass) {
        WriteLog("=== [H3] UClass::StaticClass() is NULL - too early ===\r\n");
        return cls;
    }

    std::wstring leaf = mod->ClassPath;
    {
        size_t dot = leaf.find_last_of(L'.');
        if (dot != std::wstring::npos) leaf = leaf.substr(dot + 1);
    }
    std::wstring leafLower = leaf;
    for (auto& ch : leafLower) ch = towlower(ch);

    std::wstring pkgQualified;
    if (!mod->Chain.empty()) {
        std::string pkg = mod->Chain[0];
        std::wstring wpkg(pkg.begin(), pkg.end());
        pkgQualified = wpkg + L"." + leafLower;
    }

    const wchar_t* candidates[5] = {
        mod->ClassPath.c_str(),
        (!mod->ClassPathLower.empty() && mod->ClassPathLower != mod->ClassPath)
            ? mod->ClassPathLower.c_str() : nullptr,
        pkgQualified.empty() ? nullptr : pkgQualified.c_str(),
        leaf.c_str(),
        (leafLower != leaf) ? leafLower.c_str() : nullptr,
    };

    if (mod->CachedClass) return mod->CachedClass;

    int hitForm = -1;
    void* mine = FindClassGuarded(nullptr, candidates, 5, &hitForm);

    if (mine) {
        mod->CachedClass = mine;
        const wchar_t* formName =
            hitForm == 0 ? L"exact JSON path" :
            hitForm == 1 ? L"lowercase JSON path" :
            hitForm == 2 ? L"package-qualified (lower)" :
            hitForm == 3 ? L"name-only (exact)" :
            hitForm == 4 ? L"name-only (lower)" : L"?";
        WriteDiagF("=== [H3] matched via %S ===\r\n", formName);
        if (hitForm == 2) WriteDiagF("=== [H3] class real path = \"%S\" ===\r\n", pkgQualified.c_str());
        if (hitForm >= 3) WriteDiagF("=== [H3] class leaf = \"%S\" ===\r\n",
            (hitForm == 3 ? leaf.c_str() : leafLower.c_str()));
    }

    if (mine) {
        WriteDiagF("=== [H3] SWAPPED -> %p  (\"%S\") ===\r\n\r\n",
            mine, mod->ClassPath.c_str());
        return mine;
    }

    WriteLogF("=== [H3] StaticFindObject(\"%S\") -> NULL\r\n"
        "         package loaded but no class at that path.\r\n"
        "         -> the UPK's internal class name does not match.\r\n"
        "            Check `upkrename names <file>` against \"class\". ===\r\n\r\n",
        mod->ClassPath.c_str());
    return cls;
}

static void LoadDonorAssets() {
    std::wstring path = g_GameDir + L"\\Costumes.json";
    std::ifstream f(path);
    if (!f.is_open()) {
        WriteLog("Costumes.json not found - \"donorClass\" will not resolve.\r\n"
            "  (fine if every costume specifies \"donorAsset\" directly)\r\n");
        return;
    }

    json j;
    try { f >> j; }
    catch (const std::exception& e) {
        WriteLogF("Costumes.json parse error: %s\r\n", e.what());
        return;
    }

    if (!j.contains("costumes")) { WriteLog("Costumes.json: no \"costumes\".\r\n"); return; }

    for (auto it = j["costumes"].begin(); it != j["costumes"].end(); ++it) {
        try {

            const auto& v = it.value();
            std::string hex;
            if (v.is_object()) {
                if (!v.contains("assetId")) continue;
                hex = v["assetId"].get<std::string>();
            }
            else {
                hex = v.get<std::string>();
            }
            g_DonorAssets[it.key()] = std::stoull(hex, nullptr, 16);
        }
        catch (...) {  }
    }

    WriteLogF("Costumes.json: %zu donor asset(s).\r\n", g_DonorAssets.size());
}

static void DeobfuscateConfig(std::vector<char>& b) {
    static const char* K = "MarvelHeroesCostumeConfig";
    const size_t kn = strlen(K);
    for (size_t i = 0; i < b.size(); ++i) {
        unsigned char c = (unsigned char)b[i];
        c = (unsigned char)(c ^ (unsigned char)K[i % kn]);
        c = (unsigned char)(c ^ (unsigned char)((i * 167 + 13) & 0xFF));
        b[i] = (char)c;
    }
}

static bool ReadAllBytesFile(const std::wstring& path, std::vector<char>& out) {
    std::ifstream f(path, std::ios::binary);
    if (!f.is_open()) return false;
    out.assign(std::istreambuf_iterator<char>(f), std::istreambuf_iterator<char>());
    return true;
}

static bool ReadConfigText(std::string& out) {
    std::vector<char> raw;

    if (ReadAllBytesFile(g_GameDir + L"\\CustomCostumes.mhc", raw) &&
        raw.size() > 5 &&
        raw[0] == 'M' && raw[1] == 'H' && raw[2] == 'C' && raw[3] == 'C')
    {
        std::vector<char> payload(raw.begin() + 5, raw.end());
        DeobfuscateConfig(payload);
        out.assign(payload.begin(), payload.end());
        WriteLog("config: CustomCostumes.mhc\r\n");
        return true;
    }

    if (ReadAllBytesFile(g_GameDir + L"\\CustomCostumes.json", raw)) {
        out.assign(raw.begin(), raw.end());
        WriteLog("config: CustomCostumes.json (plain)\r\n");
        return true;
    }
    return false;
}

static bool LoadConfig() {
    std::string cfgText;
    if (!ReadConfigText(cfgText)) {
        WriteLog("No config found (CustomCostumes.mhc or CustomCostumes.json).\r\n");
        return false;
    }

    json j;
    try { j = json::parse(cfgText); }
    catch (const std::exception& e) {
        WriteLogF("config parse error: %s\r\n", e.what());
        return false;
    }

    if (!j.contains("costumes") || !j["costumes"].is_array()) {
        WriteLog("CustomCostumes.json: no \"costumes\" array.\r\n");
        return false;
    }

    g_SafeMode = j.value("safeMode", true);
    if (!g_SafeMode) WriteLog("[safe] safeMode DISABLED by config.\r\n");
    LoadQuarantineAndRotateSentinel();

    g_CookedDir = g_GameDir + L"\\..\\..\\MarvelGame\\CookedPCConsole";
    {
        DWORD attr = GetFileAttributesW(g_CookedDir.c_str());
        if (attr == INVALID_FILE_ATTRIBUTES || !(attr & FILE_ATTRIBUTE_DIRECTORY)) {
            WriteLog("[cfg] WARNING: CookedPCConsole not found - package existence checks "
                     "are DISABLED for this run.\r\n");
            g_CookedDir.clear();
        }
    }

    g_Diagnostics = j.value("diagnostics", false);
    WriteLogF("[cfg] diagnostics = %s%s - %s\r\n",
        g_Diagnostics ? "true" : "false",
        j.contains("diagnostics") ? "" : "  (KEY ABSENT - using the built-in default)",
        g_Diagnostics
            ? "per-item and per-event logging ON"
            : "basic logging only; set \"diagnostics\": true for per-item detail");

    g_BandScan = j.value("bandScan", false);
    if (g_BandScan)
        WriteLog("[cfg] bandScan ON - dumping donor icon/locale band offsets per clone. "
                 "SLOW on a large install; leave off for normal play.\r\n");

    g_FxDryRun = j.value("fxDryRun", true);
    g_FxHeroKeyConditions = j.value("fxHeroKeyConditions", false);
    g_MissileProbe = j.value("missileProbe", false);
    g_HotspotFx    = j.value("hotspotFx", true);
    g_PerAvatarMesh = j.value("perAvatarMesh", false);

    g_Register     = j.value("register", true);
    g_RegisterHost = j.value("registerHost", std::string());
    g_RegisterPort = j.value("registerPort", 0);

    g_FxStackIds.clear();
    if (j.contains("fxStackIds") && j["fxStackIds"].is_array()) {
        for (auto& e : j["fxStackIds"]) {
            if (!e.is_string()) continue;
            uint64_t v = strtoull(e.get<std::string>().c_str(), nullptr, 16);
            if (v) g_FxStackIds.insert(v);
        }
    }
    if (!g_FxStackIds.empty())
        WriteLogF("[cfg] fxStackIds: %zu id(s) will be stack-traced on EVERY resolution "
                  "(read the stacks that are NOT the prefetch chain)\r\n", g_FxStackIds.size());
    if (g_MissileProbe)
        WriteLog("[cfg] missileProbe ON - [M] createMissile + [OWN] owner scan + [FXSITE] where each of our stock effect ids resolves + [FP] FireProjectile. Independent of powerProbe; leave powerProbe OFF to keep login fast.\r\n");

    WriteLogF("[cfg] fxDryRun = %s%s - %s\r\n",
             g_FxDryRun ? "TRUE" : "false",
             j.contains("fxDryRun") ? "" : " (KEY ABSENT - using the built-in default)",
             g_FxDryRun ? "redirects are COMPUTED AND DISCARDED; nothing custom can render"
                        : "redirects are LIVE");

    WriteLogF("[cfg] hotspotFx = %s%s - %s\r\n",
             g_HotspotFx ? "TRUE" : "false",
             j.contains("hotspotFx") ? "" : " (KEY ABSENT - using the built-in default)",
             g_HotspotFx
                 ? "the hotspot owner comes from the SERVER (a forged prototype ref Hook 7 "
                   "reads); no scan is involved, and no forged ids means no substitution"
                 : "FORCED OFF - hotspots render STOCK even when the server names their owner");
    WriteLogF("[cfg] perAvatarMesh = %s%s - %s\r\n",
             g_PerAvatarMesh ? "TRUE" : "false",
             j.contains("perAvatarMesh") ? "" : " (KEY ABSENT - using the built-in default)",
             g_PerAvatarMesh
                 ? "a costume with a forged CostumeUnrealClass matches ONLY that id, so the mesh "
                   "swap is PER-AVATAR. Costumes without one are unaffected."
                 : "the mesh swap is CLIENT-WIDE: another player wearing the stock DONOR costume "
                   "renders as the custom while it is armed");

    WriteLogF("[cfg] register = %s%s - %s\r\n",
             g_Register ? "TRUE" : "false",
             j.contains("register") ? "" : " (KEY ABSENT - using the built-in default)",
             g_Register
                 ? "this client will tell the server which forged hotspot ids it can decode, "
                   "so the server can withhold the others from players whose config differs"
                 : "FORCED OFF - the server falls back to the per-account flag, and a player "
                   "with the mod but WITHOUT this costume installed sees the effect vanish");
    if (g_FxHeroKeyConditions)
        WriteLog("[cfg] fxHeroKeyConditions ON - conditions that resolve under the HERO key (e.g. ground AoE, enemy debuffs) will also be redirected while the costume is worn. This is NOT per-player: other players of the same hero see those conditions custom too.\r\n");

#if PHASE0_POWER_PROBE

    g_PowerProbe = j.value("powerProbe", false);
    if (g_PowerProbe)
        WriteLog("[cfg] powerProbe ON - logging PowerPrototype::GetUnrealClass keys and each "
                 "power's own override table, once per power. "
                 "Resolve ids with: python pak\\buildeffects.py --lookup 0x...\r\n");
#endif

    g_AssetProbe = j.value("assetProbe", false);
    if (g_AssetProbe)
        WriteLog("[cfg] assetProbe ON - logging each DISTINCT AssetId GetLoadedUnrealClass "
                 "resolves, once. Resolve them with: python pak\\buildeffects.py --lookup 0x...\r\n");

    for (const auto& e : j["costumes"]) {
        try {
            CostumeMod m;
            m.Name = e.value("name", "(unnamed)");
            m.EnumIndex = e.at("enum").get<uint32_t>();
            m.CustomID = std::stoull(e.at("customId").get<std::string>(), nullptr, 16);
            m.DonorID = std::stoull(e.at("donorId").get<std::string>(), nullptr, 16);

            m.DonorClass = e.value("donorClass", std::string());

            const char* src = "";
            if (e.contains("donorAsset")) {
                m.DonorAsset = std::stoull(e["donorAsset"].get<std::string>(), nullptr, 16);
                src = " (explicit)";
            }
            else {
                if (m.DonorClass.empty()) {
                    WriteLogF("  \"%s\": neither \"donorAsset\" nor \"donorClass\" - SKIPPED\r\n",
                        m.Name.c_str());
                    continue;
                }
                auto d = g_DonorAssets.find(m.DonorClass);
                if (d == g_DonorAssets.end()) {
                    WriteLogF("  \"%s\": donorClass \"%s\" is not in Costumes.json - SKIPPED\r\n"
                        "    check the spelling, or add \"donorAsset\" directly.\r\n",
                        m.Name.c_str(), m.DonorClass.c_str());
                    continue;
                }
                m.DonorAsset = d->second;
                src = " (via donorClass)";
            }

            std::string cls = e.at("class").get<std::string>();
            m.ClassPath.assign(cls.begin(), cls.end());

            m.ClassPathLower = m.ClassPath;
            for (auto& ch : m.ClassPathLower) ch = towlower(ch);

            for (const auto& p : e.at("chain"))
                m.Chain.push_back(p.get<std::string>());

            if (e.contains("protoIcons") && e["protoIcons"].is_array()) {
                for (const auto& p : e["protoIcons"]) {
                    uint64_t off = std::stoull(p.at("off").get<std::string>(), nullptr, 16);
                    uint64_t val = std::stoull(p.at("asset").get<std::string>(), nullptr, 16);

                    if (off + 8 > PROTO_STRUCT_SIZE || (off & 7)) {
                        WriteLogF("  \"%s\": protoIcons offset 0x%llX is not an aligned qword "
                            "inside the 0x%X-byte prototype - SKIPPED\r\n",
                            m.Name.c_str(), (unsigned long long)off, PROTO_STRUCT_SIZE);
                        continue;
                    }
                    m.ProtoPatches.push_back(std::make_pair((uint32_t)off, val));

                    if (p.contains("path")) {
                        std::string ipath = p.at("path").get<std::string>();
                        if (ipath.size() > 15) {
                            WriteLogF("  \"%s\": icon path \"%s\" is %zu chars, max 15 "
                                "- SKIPPED (would need a heap write)\r\n",
                                m.Name.c_str(), ipath.c_str(), ipath.size());
                        }
                        else if (!ipath.empty()) {
                            g_IconPaths[val] = ipath;
                            WriteDiagF("  \"%s\": icon 0x%016llX -> \"%s\"\r\n",
                                m.Name.c_str(), (unsigned long long)val, ipath.c_str());
                        }
                    }
                }
            }

            if (e.contains("effects")) {
                m.ForgedAsset = ForgeCostumeAssetId(m.EnumIndex);
                m.ProtoPatches.push_back(
                    std::make_pair((uint32_t)PROTO_COSTUMEUNREALCLASS_OFF, m.ForgedAsset));
                WriteLogF("  \"%s\": custom FX opt-in -> forged CostumeUnrealClass "
                    "0x%016llX at +0x%03X  (Hook 3 also watches THIS, not just donorAsset "
                    "0x%016llX)\r\n",
                    m.Name.c_str(), (unsigned long long)m.ForgedAsset,
                    PROTO_COSTUMEUNREALCLASS_OFF, (unsigned long long)m.DonorAsset);

                uint32_t fxIdx = 0;
                for (auto& fx : e["effects"]) {
                    if (!fx.contains("from")) continue;
                    EffectRedirect r;
                    r.From = std::stoull(fx["from"].get<std::string>(), nullptr, 16);
                    if (!r.From) continue;
                    r.To      = ForgeEffectAssetId(m.EnumIndex, fxIdx, r.From);
                    r.Package = fx.value("package", std::string());
                    r.Class   = fx.value("class", std::string());
                    m.Effects[r.From] = r;

                    if (!r.Package.empty())
                        m.Chain.push_back(r.Package);

                    WriteDiagF("    fx[%u] 0x%016llX -> 0x%016llX  pkg=\"%s\"\r\n",
                        fxIdx, (unsigned long long)r.From, (unsigned long long)r.To,
                        r.Package.empty() ? "(none - report only)" : r.Package.c_str());
                    ++fxIdx;
                }
                if (m.Effects.empty())
                    WriteLogF("    (no effect entries - forged id only, this is the "
                        "isolation run)\r\n");
            }

            if (e.contains("hotspots")) {
                size_t rowsSeen = 0, rowsNoEnum = 0, rowsBad = 0;
                for (auto& h : e["hotspots"]) {
                    ++rowsSeen;
                    if (!h.is_object()) { ++rowsBad; continue; }

                    if (!h.contains("enum")) { ++rowsNoEnum; continue; }
                    if (!h.contains("forged") || !h.contains("stock")) { ++rowsBad; continue; }

                    CostumeMod::HotspotRef r;
                    try {
                        r.Forged = std::stoull(h["forged"].get<std::string>(), nullptr, 16);
                        r.Stock  = std::stoull(h["stock"].get<std::string>(), nullptr, 16);
                        r.Enum   = (uint32_t)h["enum"].get<uint64_t>();
                    }
                    catch (...) { ++rowsBad; continue; }

                    if (!r.Forged || !r.Stock || r.Enum < 100000) { ++rowsBad; continue; }
                    m.Hotspots.push_back(r);
                }
                WriteLogF("  \"%s\": %zu forged hotspot id(s) of %zu row(s) - the server names "
                    "the owner; no entity scan is used\r\n",
                    m.Name.c_str(), m.Hotspots.size(), rowsSeen);
                for (auto& r : m.Hotspots)
                    WriteDiagF("      hotspot enum %u -> forged 0x%016llX (stock 0x%016llX)\r\n",
                        r.Enum, (unsigned long long)r.Forged, (unsigned long long)r.Stock);
                if (rowsNoEnum)
                    WriteLogF("      *** %zu row(s) have NO \"enum\" - written by an OLDER "
                        "Costume Manager. Rebuild it, re-run Effects tab -> \"Sync hotspot "
                        "ids\", and restart the server. Hotspots render STOCK until then. "
                        "***\r\n", rowsNoEnum);
                if (rowsBad)
                    WriteLogF("      *** %zu row(s) MALFORMED (need forged + stock + "
                        "enum >= 100000) - ignored ***\r\n", rowsBad);
            }

            if (e.contains("displayName")) {
                std::string dname = e["displayName"].get<std::string>();
                if (!dname.empty()) {
                    const uint64_t nameId = ForgeNameId(m.EnumIndex);
                    g_NameTexts[nameId] = dname;
                    m.ProtoPatches.push_back(std::make_pair((uint32_t)PROTO_DISPLAYNAME_OFF, nameId));
                    WriteDiagF("  \"%s\": displayName \"%s\" -> forged id 0x%016llX at +0x%03X\r\n",
                        m.Name.c_str(), dname.c_str(),
                        (unsigned long long)nameId, PROTO_DISPLAYNAME_OFF);
                }
            }

            if (!m.ProtoPatches.empty())
                WriteDiagF("  \"%s\": %zu prototype patch(es) - will CLONE the donor "
                    "prototype on first lookup\r\n", m.Name.c_str(), m.ProtoPatches.size());

            if (e.contains("iconPackage")) {
                std::string ipkg = e.at("iconPackage").get<std::string>();

                if (!ipkg.empty() && !g_CookedDir.empty() && !PackageFileExists(ipkg)) {
                    WriteLogF("  \"%s\": *** ICON UPK MISSING *** \"%s.upk\" is not in "
                        "CookedPCConsole - eager load SKIPPED (icons fall back to the donor's)\r\n",
                        m.Name.c_str(), ipkg.c_str());
                }
                else if (!ipkg.empty() &&
                    std::find(g_IconPackages.begin(), g_IconPackages.end(), ipkg) == g_IconPackages.end()) {
                    g_IconPackages.push_back(ipkg);
                    WriteDiagF("  \"%s\": icon package \"%s\" queued for eager load\r\n",
                        m.Name.c_str(), ipkg.c_str());
                }
            }

            if (!g_CookedDir.empty()) {
                for (size_t ci = 0; ci < m.Chain.size(); ++ci) {
                    if (PackageFileExists(m.Chain[ci])) continue;

                    g_Unavailable.insert(ToLowerAscii(m.Name));
                    WriteLogF("  \"%s\": *** UPK MISSING *** \"%s.upk\" is not in "
                        "CookedPCConsole - this costume will NOT arm (donor renders instead)\r\n",
                        m.Name.c_str(), m.Chain[ci].c_str());
                    break;
                }
            }

            g_ByEnum[m.EnumIndex] = m;

            WriteLogF("Loaded \"%s\"  enum=%u  custom=0x%016llX  donor=0x%016llX  "
                "donorAsset=0x%016llX%s  chain=%zu%s\r\n",
                m.Name.c_str(), m.EnumIndex,
                (unsigned long long)m.CustomID,
                (unsigned long long)m.DonorID,
                (unsigned long long)m.DonorAsset, src,
                m.Chain.size(),
                IsQuarantined(m.Name) ? "   *** QUARANTINED - will not arm ***" : "");
        }
        catch (const std::exception& ex) {
            WriteLogF("  skipped a costume entry: %s\r\n", ex.what());
        }
    }

    for (auto& kv : g_ByEnum) {
        g_ByCustom[kv.second.CustomID] = &kv.second;
        if (kv.second.ForgedAsset)
            g_ByForgedAsset[kv.second.ForgedAsset] = &kv.second;
        for (auto& h : kv.second.Hotspots) {
            ForgedHotspot fh;
            fh.Mod = &kv.second;
            fh.Stock = h.Stock;
            g_ByForgedHotspot[h.Forged] = fh;
            g_ByHotspotEnum[h.Enum] = h.Forged;
        }

        for (auto& e : kv.second.Effects) {
            g_EffectByForgedAsset[e.second.To] = &e.second;
            if (e.second.From && g_EffectByStockAsset.find(e.second.From)
                                 == g_EffectByStockAsset.end()) {
                FxStockRef r;
                r.Mod = &kv.second;
                r.Fx  = &e.second;
                g_EffectByStockAsset[e.second.From] = r;
            }
        }
    }

    WriteLogF("%zu costume(s) active.\r\n", g_ByEnum.size());
    return !g_ByEnum.empty();
}

static const wchar_t* const kRegisterPath = L"/CustomCostumes/Register";

struct HttpEndpoint {
    std::wstring Host;
    int          Port   = 0;
    std::wstring Path;
    bool         Secure = false;
};

static bool ParseUrl(const std::wstring& raw, HttpEndpoint& out)
{
    std::wstring url = raw;
    if (url.empty()) return false;

    out.Secure = false;

    size_t scheme = url.find(L"://");
    if (scheme != std::wstring::npos) {
        std::wstring s = url.substr(0, scheme);
        for (wchar_t& c : s) c = (wchar_t)towlower(c);
        out.Secure = (s == L"https");
        url = url.substr(scheme + 3);
    }

    size_t slash = url.find(L'/');
    if (slash != std::wstring::npos) {
        out.Path = url.substr(slash);
        url = url.substr(0, slash);
    }
    else {
        out.Path = L"/";
    }

    size_t colon = url.find(L':');
    if (colon != std::wstring::npos) {
        out.Port = _wtoi(url.c_str() + colon + 1);
        url = url.substr(0, colon);
    }

    if (out.Port <= 0 || out.Port > 65535)
        out.Port = out.Secure ? 443 : 80;

    out.Host = url;
    return !out.Host.empty();
}

static bool SiteConfigUrlFromCommandLine(HttpEndpoint& out)
{
    const wchar_t* cmd = GetCommandLineW();
    if (!cmd) return false;

    std::wstring line(cmd);
    std::wstring lower(line);
    for (wchar_t& c : lower) c = (wchar_t)towlower(c);

    const std::wstring key = L"-siteconfigurl=";
    size_t at = lower.find(key);
    if (at == std::wstring::npos) return false;

    size_t start = at + key.size();
    size_t end = start;
    while (end < line.size() && line[end] != L' ' && line[end] != L'\t' && line[end] != L'"')
        ++end;

    return ParseUrl(line.substr(start, end - start), out);
}

static DWORD HttpRequest(const HttpEndpoint& ep, const std::string& body, std::string* outBody)
{
    DWORD status = 0;

    HINTERNET hSession = WinHttpOpen(L"MHCostumeMod/1.0",
                                     WINHTTP_ACCESS_TYPE_NO_PROXY,
                                     WINHTTP_NO_PROXY_NAME,
                                     WINHTTP_NO_PROXY_BYPASS, 0);
    if (!hSession) return 0;

    WinHttpSetTimeouts(hSession, 5000, 5000, 5000, 5000);

    HINTERNET hConnect = WinHttpConnect(hSession, ep.Host.c_str(), (INTERNET_PORT)ep.Port, 0);
    if (hConnect) {
        DWORD flags = ep.Secure ? WINHTTP_FLAG_SECURE : 0;
        HINTERNET hRequest = WinHttpOpenRequest(hConnect,
                                                body.empty() ? L"GET" : L"POST",
                                                ep.Path.c_str(), NULL,
                                                WINHTTP_NO_REFERER,
                                                WINHTTP_DEFAULT_ACCEPT_TYPES, flags);
        if (hRequest) {
            const wchar_t* headers = body.empty()
                ? WINHTTP_NO_ADDITIONAL_HEADERS
                : L"Content-Type: application/json\r\n";

            BOOL sent = WinHttpSendRequest(hRequest, headers, (DWORD)-1L,
                                           body.empty() ? WINHTTP_NO_REQUEST_DATA
                                                        : (LPVOID)body.c_str(),
                                           (DWORD)body.size(), (DWORD)body.size(), 0);

            if (sent && WinHttpReceiveResponse(hRequest, NULL)) {
                DWORD len = sizeof(status);
                if (!WinHttpQueryHeaders(hRequest,
                                         WINHTTP_QUERY_STATUS_CODE | WINHTTP_QUERY_FLAG_NUMBER,
                                         WINHTTP_HEADER_NAME_BY_INDEX,
                                         &status, &len, WINHTTP_NO_HEADER_INDEX))
                    status = 0;

                if (outBody) {

                    const size_t kMaxBody = 256 * 1024;
                    DWORD avail = 0;
                    char buf[4096];

                    while (WinHttpQueryDataAvailable(hRequest, &avail) && avail > 0) {
                        DWORD want = avail < sizeof(buf) ? avail : (DWORD)sizeof(buf);
                        DWORD got = 0;
                        if (!WinHttpReadData(hRequest, buf, want, &got) || got == 0)
                            break;
                        if (outBody->size() + got > kMaxBody)
                            break;
                        outBody->append(buf, got);
                    }
                }
            }

            WinHttpCloseHandle(hRequest);
        }
        WinHttpCloseHandle(hConnect);
    }

    WinHttpCloseHandle(hSession);
    return status;
}

static std::string SiteConfigField(const std::string& xml, const char* field)
{
    std::string needle = "name=\"";
    needle += field;
    needle += "\"";

    size_t at = xml.find(needle);
    if (at == std::string::npos) return std::string();

    size_t tagEnd = xml.find('>', at);
    if (tagEnd == std::string::npos) return std::string();

    size_t val = xml.find("value=\"", at);
    if (val == std::string::npos || val > tagEnd) return std::string();

    val += 7;
    size_t close = xml.find('"', val);
    if (close == std::string::npos || close > tagEnd) return std::string();

    return xml.substr(val, close - val);
}

static bool ResolveRegisterEndpoint(HttpEndpoint& out, std::vector<int>& ports)
{
    out.Path = kRegisterPath;
    out.Secure = false;
    ports.clear();

    if (!g_RegisterHost.empty()) {
        out.Host.assign(g_RegisterHost.begin(), g_RegisterHost.end());
        out.Port = g_RegisterPort > 0 ? g_RegisterPort : kDefaultRegisterPort;
        ports.push_back(out.Port);
        WriteDiagF("[REG-FX] endpoint from config override: %S:%d\r\n",
                  out.Host.c_str(), out.Port);
        return true;
    }

    HttpEndpoint site;
    if (!SiteConfigUrlFromCommandLine(site)) {
        WriteLog("[REG-FX] no -siteconfigurl= on the command line and no \"registerHost\" in "
                 "the config, so there is nothing to derive an address from. Skipping "
                 "registration; the server falls back to the per-account flag, which is the "
                 "pre-registration behaviour.\r\n");
        return false;
    }

    std::string xml;
    DWORD status = HttpRequest(site, std::string(), &xml);

    if (status == 200 && !xml.empty()) {
        std::string addr = SiteConfigField(xml, "AuthServerAddress");
        std::string port = SiteConfigField(xml, "AuthServerPort");
        int portN = port.empty() ? 0 : atoi(port.c_str());

        if (!addr.empty() && portN > 0 && portN <= 65535) {
            out.Host.assign(addr.begin(), addr.end());
            out.Port = g_RegisterPort > 0 ? g_RegisterPort : portN;
            ports.push_back(out.Port);

            if (g_RegisterPort > 0 && g_RegisterPort != portN)
                WriteDiagF("[REG-FX] SiteConfig says port %d, but \"registerPort\" is set to %d "
                          "- using the config value\r\n", portN, g_RegisterPort);

            WriteDiagF("[REG-FX] endpoint from SiteConfig (%S:%d%S): %s:%d\r\n",
                      site.Host.c_str(), site.Port, site.Path.c_str(),
                      addr.c_str(), out.Port);
            return true;
        }

        WriteLogF("[REG-FX] fetched SiteConfig from %S:%d%S but could not read "
                  "AuthServerAddress/AuthServerPort from it (got \"%s\"/\"%s\")\r\n",
                  site.Host.c_str(), site.Port, site.Path.c_str(),
                  addr.c_str(), port.c_str());
    }
    else {
        WriteLogF("[REG-FX] could not fetch SiteConfig from %S:%d%S (status %lu)\r\n",
                  site.Host.c_str(), site.Port, site.Path.c_str(),
                  (unsigned long)status);
    }

    out.Host = site.Host;

    if (g_RegisterPort > 0) {
        out.Port = g_RegisterPort;
        ports.push_back(g_RegisterPort);
        WriteDiagF("[REG-FX] falling back to the -siteconfigurl= host with the configured port: "
                  "%S:%d\r\n", out.Host.c_str(), out.Port);
    }
    else {
        ports.push_back(kDefaultRegisterPort);
        ports.push_back(kAltRegisterPort);
        if (site.Port != 80 && site.Port != 443)
            ports.push_back(site.Port);

        out.Port = ports[0];
        WriteDiagF("[REG-FX] SiteConfig was not readable, so the port is UNKNOWN - trying %d, %d"
                  "%s on %S. Set \"registerPort\" in CustomCostumes.json to skip this, or have "
                  "the server publish SiteConfig so it can be read properly.\r\n",
                  kDefaultRegisterPort, kAltRegisterPort,
                  ports.size() > 2 ? " and the SiteConfig port" : "",
                  out.Host.c_str());
    }

    return true;
}

static std::string BuildRegisterBody()
{

    char sid[40];
    sprintf_s(sid, sizeof(sid), "\"0x%016llX\",", (unsigned long long)g_SessionId);

    std::string body = "{\"sessionId\":";
    body += sid;
    body += "\"forgedRefs\":[";

    bool first = true;
    for (auto& kv : g_ByForgedHotspot) {
        char buf[32];
        sprintf_s(buf, sizeof(buf), "\"0x%016llX\"", (unsigned long long)kv.first);
        if (!first) body += ",";
        body += buf;
        first = false;
    }

    body += "]}";
    return body;
}

static const char* RegisterStatusMeaning(DWORD status)
{
    if (status == 0)   return "no reply - server not running, wrong port, or the web frontend "
                              "is bound to localhost only (see [WebFrontend] Address)";
    if (status == 404) return "no such endpoint - the SERVER is older than this DLL, or that "
                              "port belongs to something else";
    if (status == 400) return "the server rejected the body";
    if (status == 429) return "the server is at capacity";
    return "unexpected status";
}

static DWORD WINAPI RegisterThread(LPVOID)
{
    HttpEndpoint ep;
    std::vector<int> ports;

    if (!ResolveRegisterEndpoint(ep, ports) || ports.empty())
        return 0;

    for (int waited = 0; g_SessionId == 0 && waited < REGISTER_SESSION_WAIT_MS;
         waited += REGISTER_SESSION_POLL_MS)
        Sleep(REGISTER_SESSION_POLL_MS);

    if (g_SessionId == 0) {
        WriteLogF("[REG-FX] no session after %d s - NOT registering. The server falls back to "
                  "the per-account flag, which is the pre-registration behaviour.\r\n",
                  REGISTER_SESSION_WAIT_MS / 1000);
        return 0;
    }

    WriteLogF("[REG-FX] session 0x%016llX - registering %zu decodable forged id(s)\r\n",
              (unsigned long long)g_SessionId, g_ByForgedHotspot.size());

    std::string body = BuildRegisterBody();

    static const int kBackoffMs[] = { 5000, 10000, 20000, 30000, 30000, 30000,
                                      30000, 30000, 30000, 30000, 30000 };
    const int kAttempts = (int)(sizeof(kBackoffMs) / sizeof(kBackoffMs[0]));
    DWORD lastStatus = 0;

    for (int attempt = 1; attempt <= kAttempts; ++attempt) {

        for (size_t i = 0; i < ports.size(); ++i) {
            ep.Port = ports[i];
            DWORD status = HttpRequest(ep, body, nullptr);
            lastStatus = status;

            if (status == 200) {
                WriteLogF("[REG-FX] registered %zu decodable forged hotspot id(s) with "
                          "%S:%d%S  (attempt %d)\r\n",
                          g_ByForgedHotspot.size(), ep.Host.c_str(), ep.Port,
                          ep.Path.c_str(), attempt);

                ports.assign(1, ep.Port);
                return 0;
            }

            if (ports.size() > 1)
                WriteDiagF("[REG-FX]   %S:%d -> %lu (%s)\r\n",
                          ep.Host.c_str(), ep.Port, (unsigned long)status,
                          RegisterStatusMeaning(status));
        }

        if (attempt < kAttempts) {
            int waitMs = kBackoffMs[attempt - 1];

            if (attempt <= 3)
                WriteDiagF("[REG-FX] attempt %d/%d to %S%S found nothing (last status %lu) - "
                          "retrying in %ds\r\n",
                          attempt, kAttempts, ep.Host.c_str(), ep.Path.c_str(),
                          (unsigned long)lastStatus, waitMs / 1000);
            Sleep(waitMs);
        }
        else {
            WriteLogF("[REG-FX] GAVE UP after %d attempts to %S%S - last status %lu (%s). "
                      "The server falls back to the per-account flag, so custom FX still works "
                      "here; what is lost is the protection for OTHER players whose configs "
                      "differ from this one.\r\n",
                      kAttempts, ep.Host.c_str(), ep.Path.c_str(),
                      (unsigned long)lastStatus, RegisterStatusMeaning(lastStatus));
        }
    }

    return 0;
}

static DWORD WINAPI MainThread(LPVOID) {
    InitializeCriticalSection(&g_LogLock);

    wchar_t exePath[MAX_PATH];
    GetModuleFileNameW(NULL, exePath, MAX_PATH);
    g_GameDir = exePath;
    g_GameDir = g_GameDir.substr(0, g_GameDir.find_last_of(L"\\/"));

    g_Base = (uint8_t*)GetModuleHandleW(NULL);

    if (HMODULE ntdll = GetModuleHandleW(L"ntdll.dll"))
        g_CaptureStack = (CaptureStackBackTrace_t)GetProcAddress(ntdll, "RtlCaptureStackBackTrace");

    WriteLog("\r\n=== Custom Costume Coexistence ===\r\n");

    WriteLogF("[cfg] base = %p  pid %lu  log \"%S\"\r\n", g_Base,
              (unsigned long)GetCurrentProcessId(), g_LogPath.c_str());

    LoadDonorAssets();
    if (!LoadConfig()) { WriteLog("No costumes - hooks not installed.\r\n"); return 0; }

    pLoader = (PackageLoader_t)(g_Base + RVA_PACKAGELOADER);
    pFNameInit = (FNameInitA_t)(g_Base + RVA_FNAME_INIT_ANSI);
    pStaticFindObject = (StaticFindObject_t)(g_Base + RVA_STATICFINDOBJECT);

    if (MH_Initialize() != MH_OK) { WriteLog("MH_Initialize failed.\r\n"); return 0; }

    struct { void* target; void* detour; void** orig; const char* name; } hooks[] = {
        { g_Base + RVA_PROPERTY_DISPATCH,    &MyPropDispatch,       (void**)&OrigPropDispatch, "Hook 0 PropertyDispatch" },
        { g_Base + RVA_GETPROTOIDFROMENUM,   &MyGetProtoIdFromEnum, (void**)&OrigGetProtoId,   "Hook 1 GetPrototypeIdFromEnum" },
        { g_Base + RVA_LOOKUPPROTORECORD,    &MyLookupProtoRecord,  (void**)&OrigLookupProto,  "Hook 2 LookupPrototypeRecord" },
        { g_Base + RVA_GETPROTODATAREFRECORD,&MyGetProtoDataRefRecord,(void**)&OrigGetProtoDataRefRec,"Hook 7 GetPrototypeDataRefRecord" },
        { g_Base + RVA_PACKAGELOOP,          &MyPackageLoop,        (void**)&OrigLoop,         "B2     PackageLoop" },
        { g_Base + RVA_FINDCACHEDPKGINFO,    &MyFindPkgInfo,        (void**)&OrigFindPkgInfo,  "Hook 4 FindCachedPackageInfo" },
        { g_Base + RVA_GETLOADEDUNREALCLASS, &MyGLUC,               (void**)&OrigGLUC,         "Hook 3 GetLoadedUnrealClass" },
        { g_Base + RVA_GETPROTOFROMENUMVALUE,&MyGetProtoFromEnumValue,(void**)&OrigGetProtoFromEnum,"Hook 5 GetPrototypeFromEnumValue" },
        { g_Base + RVA_GETPROTOENUMVALUE,    &MyGetProtoEnumValue,  (void**)&OrigGetProtoEnumValue,"Hook 6 GetPrototypeEnumValue" },
        { g_Base + RVA_VERIFYFAIL,           &MyVerifyFail,         (void**)&OrigVerifyFail,      "Hook D Verify::VerifyFail (diagnostic)" },
        { g_Base + RVA_GETICONPATH,          &MyGetIconPath,        (void**)&OrigGetIconPath,     "Hook I GetIconPath (diagnostic)" },
        { g_Base + RVA_GETLOCALESTRING,      &MyGetLocaleString,    (void**)&OrigGetLocaleString, "Hook L Locale::GetLocaleString (diagnostic)" },
        { g_Base + RVA_POWERGETUNREALCLASS,  &MyPowerGetUnrealClass,(void**)&OrigPowerGetUnrealClass, "Hook P PowerPrototype::GetUnrealClass (per-costume FX)" },
        { g_Base + RVA_CONDGETUNREALCLASS,   &MyCondGetUnrealClass, (void**)&OrigCondGetUnrealClass,  "Hook C ConditionPrototype::GetUnrealClass (per-costume FX)" },

        { g_Base + RVA_CREATEMISSILE,        &MyCreateMissile,      (void**)&OrigCreateMissile,      "Hook M  MissilePower::createMissile (per-avatar FX)" },
        { g_Base + RVA_HOTSPOTGETCLASS,     &MyHotspotGetClass,    (void**)&OrigHotspotGetClass,    "Hook HS worldEntity class resolve (per-avatar hotspot FX)" },

        { g_Base + RVA_PROJECTILEGETCLASS,  &MyProjectileGetClass, (void**)&OrigProjectileGetClass, "Hook MP projectile/missile class resolve (per-avatar FX)" },

        { g_Base + RVA_SETAUTHTICKET,       &MySetAuthTicket,      (void**)&OrigSetAuthTicket,      "Hook AT LoginManager::SetAuthTicket (sessionId probe)" },
#if PHASE0_POWER_PROBE
        { g_Base + RVA_FIREPROJECTILE,       &MyFireProjectile,     (void**)&OrigFireProjectile,     "Hook FP UPowerFXProjectile::FireProjectile (probe)" },
#endif
    };

    for (auto& h : hooks) {
        if (MH_CreateHook(h.target, h.detour, h.orig) == MH_OK &&
            MH_EnableHook(h.target) == MH_OK)
            WriteLogF("[+] %s\r\n", h.name);
        else
            WriteLogF("[!] %s FAILED\r\n", h.name);
    }

    Hook1_Trampoline = (void*)OrigGetProtoId;
    WriteLogF("[i] Hook1_Trampoline = %p\r\n", Hook1_Trampoline);

    if (g_Register) {
        HANDLE h = CreateThread(nullptr, 0, RegisterThread, nullptr, 0, nullptr);
        if (h) CloseHandle(h);
        else WriteLog("[REG-FX] could not start the registration thread - skipping "
                      "(the server falls back to the per-account flag).\r\n");
    }

    WriteLog("Ready. Waiting for a costume change.\r\n\r\n");
    return 0;
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) {
        DisableThreadLibraryCalls(hModule);
        CreateThread(nullptr, 0, MainThread, nullptr, 0, nullptr);
    }
    return TRUE;
}
