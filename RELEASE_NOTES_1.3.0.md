# Intelbras Camera Center v1.3.0 — Universal Media Engine

## Correção principal

Corrigido o caso em que um dispositivo era descoberto na rede, mas o botão Reproduzir não iniciava o vídeo porque o aplicativo assumia um único caminho RTSP.

## Universal Stream Resolver

Nova negociação automática em camadas:

1. URL configurada pelo usuário.
2. ONVIF Media GetStreamUri.
3. ONVIF Media2 GetStreamUri.
4. biblioteca de caminhos RTSP de fabricantes e famílias globais.
5. autenticação RTSP Basic/Digest para validar caminhos quando a credencial já foi fornecida.
6. tentativa RTSP/TCP e transporte automático/UDP.
7. fallback HTTP/MJPEG.

Perfis incluídos na biblioteca de resolução:
- Intelbras / Dahua / Amcrest.
- Hikvision / HiLook.
- Axis.
- Reolink.
- Uniview / UNV.
- TP-Link / Tapo.
- Foscam.
- padrões genéricos RTSP/H.264/MJPEG.

## Vídeo e áudio

- LibVLC mantém o áudio habilitado automaticamente quando o stream contém faixa de áudio.
- volume inicial 100%.
- novo botão Áudio/Mudo por câmera.
- H.264/H.265 e codecs suportados pelo runtime LibVLC.
- ONVIF Profile T/S e Media/Media2.
- RTSP/RTSPS.
- RTP sobre RTSP TCP e fallback automático.
- HTTP/HLS/MJPEG e SRT quando suportados pelo runtime.

## Segurança

Nenhuma senha é descoberta, quebrada ou contornada.
Dispositivos protegidos continuam usando o fluxo de acesso único com credencial legítima armazenada pelo Windows DPAPI.

## Distribuição

- Intelbras-Camera-Center-Setup-v1.3.0.exe
- Intelbras-Camera-Center-Portable-v1.3.0.zip
- Intelbras-Camera-Center.exe
- SHA256SUMS.txt
