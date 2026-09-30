# Security

## Credentials

Camera credentials are protected with Windows DPAPI and are not persisted as plaintext.

Do not submit real camera IPs, passwords, access keys, RTSP URLs containing credentials, or exported configuration files in public issues.

## Network scope

The application performs ONVIF WS-Discovery on the local network only when the user presses the discovery button. Event monitoring connects only to devices explicitly stored in the local device list.

## Reporting

For a security issue, avoid posting secrets or exploit data that exposes a real installation. Open a minimal issue asking for a private contact path.
