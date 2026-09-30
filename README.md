# Intelbras Camera Center v1.2.0 — Zero-Touch Discovery

VMS/NVR desktop para Windows voltado a câmeras IP, DVRs, NVRs Intelbras e ecossistemas ONVIF/RTSP.

> Projeto comunitário independente. Não é um produto oficial nem afiliado à Intelbras S.A.

## Download

A versão recomendada fica em **Releases**:

- Intelbras-Camera-Center-Setup-v1.2.0.exe
- Intelbras-Camera-Center-Portable-v1.2.0.zip
- Intelbras-Camera-Center.exe
- SHA256SUMS.txt

A pasta `dist/` da branch `main` também recebe o instalador, launcher e Portable expandido após cada build válida.

## Stack

- .NET 8 / WPF
- WPF-UI 4.3.0 / Fluent
- LibVLCSharp + LibVLC
- Intelbras/Dahua CGI
- ONVIF WS-Discovery
- ONVIF Media/PTZ/Events com WS-Security PasswordDigest
- go2rtc
- Frigate API
- Home Assistant REST API
- API local loopback
- métricas Prometheus
- Windows DPAPI

## v1.2.0

### Vídeo e transporte

- RTSP e RTSPS
- URLs HTTP/HLS
- SRT quando disponível no runtime LibVLC
- URLs de stream personalizadas
- H.264/H.265 via LibVLC
- main stream e substream
- snapshots
- gravação MPEG-TS
- cache de baixa latência

### ONVIF

- WS-Discovery
- descoberta de serviços Media, PTZ e Events
- WS-Security UsernameToken PasswordDigest
- obtenção automática de profile token
- ContinuousMove
- Stop
- pan/tilt/zoom diretamente em cada tile
- arquitetura alinhada aos Profiles T, G e M

### go2rtc / WebRTC

A Central de Integrações conversa com a API do go2rtc e pode publicar uma câmera cadastrada como stream do gateway. Isso permite aproveitar os formatos oferecidos pelo go2rtc, incluindo WebRTC/WHEP, WHIP, HLS, MP4, MJPEG, RTMP, HomeKit e FFmpeg conforme a configuração do gateway.

### Frigate

Teste de conectividade pela API oficial de versão do Frigate, preparando o VMS para uso ao lado de NVR/AI.

### Home Assistant

- URL configurável
- Long-Lived Access Token
- autenticação Bearer
- token criptografado pelo Windows DPAPI
- teste da REST API pela interface

### API local e observabilidade

Por padrão em `127.0.0.1:17777`:

- `GET /health`
- `GET /api/cameras`
- `GET /metrics`

A API não expõe senhas, tokens ou URLs contendo credenciais.

## Segurança

- credenciais protegidas com DPAPI;
- token do Home Assistant protegido com DPAPI;
- execução sem privilégio administrativo;
- API local vinculada apenas ao loopback;
- sem logs de senhas/tokens;
- hashes SHA-256 produzidos a cada build.

## Build e Release

O GitHub Actions executa:

`restore → build → publish → launcher → installer → Portable ZIP → SHA-256 → GitHub Release → dist/main`

## Arquivos locais

`%LOCALAPPDATA%\IntelbrasCameraCenter`

- `config\devices.json`
- `config\integrations.json`
- `recordings\`
- `snapshots\`

## Licença

MIT para o código original. Dependências mantêm suas respectivas licenças. Veja `THIRD_PARTY_NOTICES.md`.
