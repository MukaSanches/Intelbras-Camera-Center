# Intelbras Camera Center

Intelbras Camera Center é um VMS/NVR desktop para Windows criado para centralizar câmeras IP, DVRs e NVRs Intelbras e equipamentos compatíveis com RTSP/ONVIF.

> Projeto comunitário independente. Não é um produto oficial nem afiliado à Intelbras S.A.

## Download direto pela branch main

Após o workflow de build concluir, a pasta dist contém:

- Intelbras-Camera-Center-Setup.exe — instalador Windows.
- Intelbras-Camera-Center.exe — launcher da versão Portable.
- Portable/ — aplicação portátil completa.
- SHA256SUMS.txt — hashes SHA-256 dos binários.

O instalador verifica o .NET 8 Desktop Runtime x64 e, quando necessário, baixa o runtime oficial da Microsoft.

## Arquitetura

- .NET 8 / WPF.
- WPF-UI 4.3.0 como toolkit visual Fluent/Windows moderno.
- LibVLCSharp.WPF 3.10.1 + LibVLC 3.0.24 para RTSP, H.264/H.265 e reprodução multimídia.
- ONVIF WS-Discovery implementado nativamente para localização automática de dispositivos.
- Intelbras/Dahua-compatible CGI para informações, configuração e stream de eventos.
- Windows DPAPI para proteger senhas localmente.
- Build automatizado com GitHub Actions + Inno Setup.

## Funcionalidades implementadas na base 1.0

### Monitoramento

- mosaico de câmeras;
- stream principal e substream;
- RTSP sobre TCP;
- cache de baixa latência;
- snapshots PNG;
- gravação local por câmera em MPEG-TS;
- suporte a URL RTSP personalizada;
- padrão Intelbras/Dahua /cam/realmonitor?channel=N&subtype=0/1.

### Descoberta e dispositivos

- descoberta ONVIF/WS-Discovery via multicast;
- câmera, DVR, NVR, Mibo e equipamento genérico;
- múltiplos canais;
- portas RTSP e HTTP configuráveis;
- cadastro e edição local;
- consulta magicBox.cgi;
- cliente genérico para configManager.cgi.

### Eventos inteligentes

O monitor CGI usa eventManager.cgi?action=attach&codes=[All] e possui reconexão automática. A interface classifica eventos como movimento, IVS, cruzamento de linha, intrusão/região, tráfego/LPR e eventos faciais.

A disponibilidade depende do modelo e firmware do equipamento.

### Segurança

- senha nunca é salva em texto puro;
- credenciais protegidas por CryptProtectData / Windows DPAPI;
- dados vinculados ao usuário Windows atual;
- aplicação executada sem privilégios administrativos;
- nenhum log grava usuário/senha ou URI autenticada.

## Pastas locais

%LOCALAPPDATA%\IntelbrasCameraCenter

- config\devices.json
- recordings\
- snapshots\

## Build

Requisitos de desenvolvimento:

- Windows 10/11 x64;
- Visual Studio 2022 ou .NET SDK 8;
- Git.

Comandos:

dotnet restore Intelbras.CameraCenter.sln

dotnet build Intelbras.CameraCenter.sln -c Release

dotnet run --project src/Intelbras.CameraCenter.App

O workflow .github/workflows/build-windows.yml gera os binários e publica dist na própria branch main.

## Compatibilidade planejada

A arquitetura foi desenhada para absorver gradualmente Intelbras VIP, Mibo iC/iM compatíveis com RTSP/ONVIF, DVR/NVR Intelbras, LPR, IVS, PTZ ONVIF, playback remoto, timeline, áudio bidirecional quando exposto, Frigate/go2rtc, Home Assistant, mapa de câmeras, regras, automações, multi-monitor, video wall, perfis de operador e exportação de evidências.

## Fontes e projetos estudados

O projeto foi desenhado após estudar iniciativas comunitárias relacionadas a Intelbras, RTSP, ONVIF, DVR/NVR, Mibo e VMS. Código de projetos de terceiros não foi simplesmente copiado para este repositório. Protocolos e conceitos foram reimplementados para manter uma base coerente e auditável.

Veja THIRD_PARTY_NOTICES.md.

## Licença

MIT para o código original deste repositório. Dependências mantêm suas próprias licenças.
