# Intelbras Camera Center v1.2.0 — Zero-Touch Discovery

## Descoberta mundial de vídeo

- ONVIF / WS-Discovery.
- SSDP / UPnP.
- varredura controlada da LAN em redes IPv4 locais.
- detecção de RTSP/RTSPS e serviços de vídeo.
- fingerprint de HTTP/HTTPS e portas de ecossistemas Intelbras/Dahua-compatible.
- identificação de câmera, DVR, NVR e endpoint genérico quando houver evidência suficiente.
- descoberta automática ao iniciar o aplicativo.
- dispositivos protegidos aparecem no inventário mesmo antes da autenticação.

## Acesso único

A v1.2.0 não tenta contornar autenticação.

Quando um dispositivo exige senha:
- a credencial é solicitada somente quando necessária;
- é armazenada criptografada com Windows DPAPI;
- é reutilizada automaticamente nos canais do mesmo gravador;
- o aplicativo tenta validar por Intelbras/Dahua CGI ou ONVIF;
- depois da validação, tenta identificar a quantidade de canais do DVR/NVR e cria os canais automaticamente.

Não há brute force, tentativa de senhas padrão nem bypass de autenticação.

## Ecossistemas

Mantidos e ampliados:
- Intelbras CGI.
- ONVIF Media/PTZ/Events.
- RTSP/RTSPS.
- HTTP/HLS e SRT via LibVLC quando suportados.
- go2rtc / WebRTC / WHEP / WHIP.
- Frigate.
- Home Assistant.
- Prometheus.
- API local loopback.

## Distribuição

- Intelbras-Camera-Center-Setup-v1.2.0.exe
- Intelbras-Camera-Center-Portable-v1.2.0.zip
- Intelbras-Camera-Center.exe
- SHA256SUMS.txt

Projeto comunitário independente, não afiliado à Intelbras S.A.
