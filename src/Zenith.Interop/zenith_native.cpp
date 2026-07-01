#ifdef _WIN32
#include <windows.h>
#include <dxgi.h>
#include <mmdeviceapi.h> // Left for now, in case other things need COM
#include <functiondiscoverykeys_devpkey.h>
#include <mfapi.h>
#include <mfidl.h>
#include <vector>
#include <string>
#include <sstream>
#include <iomanip>

#pragma comment(lib, "dxgi.lib")
#pragma comment(lib, "ole32.lib")
#pragma comment(lib, "mfplat.lib")
#pragma comment(lib, "mfuuid.lib")
#pragma comment(lib, "user32.lib")
#pragma comment(lib, "mf.lib")

// Helper to escape JSON string
std::string EscapeJsonString(const std::string& input) {
    std::ostringstream ss;
    for (char c : input) {
        switch (c) {
            case '\"': ss << "\\\""; break;
            case '\\': ss << "\\\\"; break;
            case '\b': ss << "\\b"; break;
            case '\f': ss << "\\f"; break;
            case '\n': ss << "\\n"; break;
            case '\r': ss << "\\r"; break;
            case '\t': ss << "\\t"; break;
            default:
                if (c >= 0 && c < 32) {
                    ss << "\\u" << std::hex << std::setw(4) << std::setfill('0') << (int)c;
                } else {
                    ss << c;
                }
                break;
        }
    }
    return ss.str();
}

std::string Utf8Encode(const std::wstring& wstr) {
    if (wstr.empty()) return std::string();
    int size_needed = WideCharToMultiByte(CP_UTF8, 0, &wstr[0], (int)wstr.size(), NULL, 0, NULL, NULL);
    std::string strTo(size_needed, 0);
    WideCharToMultiByte(CP_UTF8, 0, &wstr[0], (int)wstr.size(), &strTo[0], size_needed, NULL, NULL);
    return strTo;
}

struct NativeSource {
    std::string SourceID;
    std::string DisplayName;
    std::string Resolution;
    std::string SourceType;
    int X;
    int Y;
};

// Callback for window enumeration
BOOL CALLBACK EnumWindowsProc(HWND hwnd, LPARAM lParam) {
    std::vector<NativeSource>* sources = reinterpret_cast<std::vector<NativeSource>*>(lParam);
    
    // Filter visible and active windows
    if (!IsWindowVisible(hwnd)) return TRUE;
    
    int length = GetWindowTextLengthW(hwnd);
    if (length == 0) return TRUE;
    
    std::wstring title(length, L'\0');
    GetWindowTextW(hwnd, &title[0], length + 1);
    
    // Remove empty titles
    if (title.empty() || title == L"Program Manager") return TRUE;
    
    // Filter out tool windows
    LONG style = GetWindowLongW(hwnd, GWL_EXSTYLE);
    if (style & WS_EX_TOOLWINDOW) return TRUE;
    
    RECT rect;
    GetWindowRect(hwnd, &rect);
    int width = rect.right - rect.left;
    int height = rect.bottom - rect.top;
    if (width <= 100 || height <= 100) return TRUE; // Filter too small windows
    
    std::stringstream res;
    res << width << "x" << height;
    
    std::stringstream id;
    id << "window:" << (uintptr_t)hwnd;
    
    sources->push_back({
        id.str(),
        Utf8Encode(title),
        res.str(),
        "Window",
        (int)rect.left,
        (int)rect.top
    });
    
    return TRUE;
}

// Global thread-safe buffer pointer
static std::string g_LastJsonResult;

extern "C" {
    __declspec(dllexport) const char* GetAvailableSources() {
        std::vector<NativeSource> sources;

        // 1. Enumerate Displays (DXGI)
        IDXGIFactory1* factory = nullptr;
        if (SUCCEEDED(CreateDXGIFactory1(__uuidof(IDXGIFactory1), (void**)&factory))) {
            IDXGIAdapter1* adapter = nullptr;
            UINT adapterIndex = 0;
            while (factory->EnumAdapters1(adapterIndex, &adapter) == S_OK) {
                IDXGIOutput* output = nullptr;
                UINT outputIndex = 0;
                while (adapter->EnumOutputs(outputIndex, &output) == S_OK) {
                    DXGI_OUTPUT_DESC desc;
                    output->GetDesc(&desc);
                    
                    int width = desc.DesktopCoordinates.right - desc.DesktopCoordinates.left;
                    int height = desc.DesktopCoordinates.bottom - desc.DesktopCoordinates.top;
                    
                    std::stringstream res;
                    res << width << "x" << height;
                    
                    sources.push_back({
                        Utf8Encode(desc.DeviceName),
                        "Screen " + std::to_string(sources.size() + 1) + " (" + res.str() + ")",
                        res.str(),
                        "Screen",
                        (int)desc.DesktopCoordinates.left,
                        (int)desc.DesktopCoordinates.top
                    });
                    
                    output->Release();
                    outputIndex++;
                }
                adapter->Release();
                adapterIndex++;
            }
            factory->Release();
        }

        // 2. Enumerate Windows (EnumWindows)
        EnumWindows(EnumWindowsProc, reinterpret_cast<LPARAM>(&sources));

        // Initialize COM for Audio/Webcam if not already initialized
        HRESULT hrCom = CoInitializeEx(NULL, COINIT_APARTMENTTHREADED);

        // WASAPI Audio enumeration has been moved to C# using NAudio.
        // 4. Enumerate Webcams (Media Foundation)
        if (SUCCEEDED(MFStartup(MF_VERSION))) {
            IMFAttributes* attributes = nullptr;
            if (SUCCEEDED(MFCreateAttributes(&attributes, 1))) {
                if (SUCCEEDED(attributes->SetGUID(MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE, MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_GUID))) {
                    IMFActivate** devices = nullptr;
                    UINT32 count = 0;
                    if (SUCCEEDED(MFEnumDeviceSources(attributes, &devices, &count))) {
                        for (UINT32 i = 0; i < count; i++) {
                            LPWSTR nameStr = nullptr;
                            UINT32 nameLength = 0;
                            std::string friendlyName = "Unknown Camera";
                            if (SUCCEEDED(devices[i]->GetAllocatedString(MF_DEVSOURCE_ATTRIBUTE_FRIENDLY_NAME, &nameStr, &nameLength))) {
                                friendlyName = Utf8Encode(nameStr);
                                CoTaskMemFree(nameStr);
                            }

                            LPWSTR symStr = nullptr;
                            UINT32 symLength = 0;
                            std::string symLink = "";
                            if (SUCCEEDED(devices[i]->GetAllocatedString(MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_SYMBOLIC_LINK, &symStr, &symLength))) {
                                symLink = Utf8Encode(symStr);
                                CoTaskMemFree(symStr);
                            }

                            sources.push_back({
                                symLink,
                                friendlyName,
                                "",
                                "Webcam",
                                0, 0
                            });

                            devices[i]->Release();
                        }
                        CoTaskMemFree(devices);
                    }
                }
                attributes->Release();
            }
            MFShutdown();
        }

        if (SUCCEEDED(hrCom)) {
            CoUninitialize();
        }

        // Format to JSON
        std::stringstream json;
        json << "[";
        for (size_t i = 0; i < sources.size(); ++i) {
            if (i > 0) json << ",";
            json << "{"
                 << "\"SourceID\":\"" << EscapeJsonString(sources[i].SourceID) << "\","
                 << "\"DisplayName\":\"" << EscapeJsonString(sources[i].DisplayName) << "\","
                 << "\"Resolution\":\"" << EscapeJsonString(sources[i].Resolution) << "\","
                 << "\"SourceType\":\"" << EscapeJsonString(sources[i].SourceType) << "\","
                 << "\"X\":" << sources[i].X << ","
                 << "\"Y\":" << sources[i].Y
                 << "}";
        }
        json << "]";

        g_LastJsonResult = json.str();
        return g_LastJsonResult.c_str();
    }
}
#else
extern "C" {
    const char* GetAvailableSources() {
        return "[]";
    }
}
#endif
