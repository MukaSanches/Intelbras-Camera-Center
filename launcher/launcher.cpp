#include <windows.h>
#include <shellapi.h>
#include <string>

#pragma comment(lib, "user32.lib")
#pragma comment(lib, "shell32.lib")

int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR, int)
{
    wchar_t modulePath[MAX_PATH] = {};
    if (!GetModuleFileNameW(nullptr, modulePath, MAX_PATH))
        return 1;

    std::wstring base(modulePath);
    auto slash = base.find_last_of(L"\\/");
    if (slash != std::wstring::npos)
        base.resize(slash + 1);

    std::wstring target = base + L"Portable\\Intelbras-Camera-Center.exe";

    DWORD attrs = GetFileAttributesW(target.c_str());
    if (attrs == INVALID_FILE_ATTRIBUTES)
    {
        MessageBoxW(nullptr,
            L"A pasta Portable não foi encontrada ao lado deste executável.\n\n"
            L"Baixe o repositório completo ou use o instalador.",
            L"Intelbras Camera Center",
            MB_OK | MB_ICONERROR);
        return 2;
    }

    HINSTANCE result = ShellExecuteW(nullptr, L"open", target.c_str(), nullptr,
        (base + L"Portable").c_str(), SW_SHOWNORMAL);

    if ((INT_PTR)result <= 32)
    {
        MessageBoxW(nullptr,
            L"Não foi possível iniciar o Intelbras Camera Center.\n"
            L"Confirme se o .NET 8 Desktop Runtime x64 está instalado.",
            L"Intelbras Camera Center",
            MB_OK | MB_ICONERROR);
        return 3;
    }

    return 0;
}
