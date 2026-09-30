# Intelbras Camera Center v1.1.0 — Global Interop

## Principais novidades

- ONVIF PTZ com WS-Security UsernameToken Digest e descoberta de serviços Media/PTZ/Events.
- Base orientada a ONVIF Profile T, G e M.
- Player genérico para RTSP/RTSPS e URLs suportadas pelo LibVLC, incluindo HTTP/HLS e SRT quando disponíveis no runtime.
- Integração nativa com go2rtc:
  - detecção da API;
  - publicação de streams;
  - gateway para WebRTC/WHEP, WHIP, HLS, MP4, MJPEG, RTMP, HomeKit e FFmpeg.
- Integração com Frigate via API de versão.
- Integração com Home Assistant via REST API e Bearer Token protegido pelo Windows DPAPI.
- API local somente em loopback:
  - GET /health
  - GET /api/cameras
  - GET /metrics em formato Prometheus.
- Central gráfica de Integrações.
- PTZ diretamente em cada tile de câmera.
- Release automatizada com Installer, Portable ZIP, launcher e SHA-256.

## Segurança

- Credenciais das câmeras e token do Home Assistant ficam protegidos com Windows DPAPI.
- API local escuta apenas em 127.0.0.1.
- Endpoints locais nunca retornam senhas, tokens ou URLs autenticadas.

## Distribuição

A Release contém:
- Intelbras-Camera-Center-Setup-v1.1.0.exe
- Intelbras-Camera-Center-Portable-v1.1.0.zip
- Intelbras-Camera-Center.exe
- SHA256SUMS.txt

Projeto comunitário independente, não afiliado à Intelbras S.A.
