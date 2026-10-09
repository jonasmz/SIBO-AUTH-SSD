# Temporary frontend (login and password recovery)

Static stand-in for the Angular application until it exists. Point `FRONTEND_STATIC_HOST_PATH` at this
directory. It talks only to the public `/auth/*` URLs of the same origin.

- Views: login, forgot password, reset password (the emailed code is pasted there) and a minimal session
  page with logout. Hash routing (`#/login`, `#/forgot`, `#/reset`).
- UI: Pixel Lite Bootstrap 5 UI Kit 4.1.0 (MIT, `LICENSE-pixel-bootstrap-ui-kit`), compiled from
  https://github.com/themesberg/pixel-bootstrap-ui-kit at commit `8d4e535` with Bootstrap 5.0.1 and
  Font Awesome Free 5.11.2 (solid icons only). The Google Fonts import was removed, so no external
  request is made and the system font is used.
- Replace this directory's contents with the compiled Angular files when they are available.
