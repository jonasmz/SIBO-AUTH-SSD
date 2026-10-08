# Roadmap de Implementación — Authentication API con GitHub Spec-Kit

**Documento:** Roadmap de desarrollo y seguimiento  
**Proyecto:** Authentication API  
**Versión:** 1.1  
**Fecha:** 2026-10-06  
**Documento de referencia:** `SRS_Authentication_API_v1.1.md`  
**Metodología:** desarrollo incremental mediante GitHub Spec-Kit  
**Estrategia:** crecimiento vertical por capacidades completas  
**Cantidad de fases:** 8  

## Control de cambios

| Versión | Fecha | Cambio |
|---|---|---|
| 1.0 | 2026-10-06 | Roadmap inicial de 8 fases verticales. |
| 1.1 | 2026-10-06 | Alineación con SRS v1.1: persistencia independiente del ciclo de vida de Compose, supervivencia ante `docker compose down -v`, backup/restore de SQLite y validación explícita del reverse proxy. |

---

# 1. Propósito

Este documento define el roadmap de implementación de Authentication API a partir de la SRS del proyecto.

Su objetivo es permitir:

- ejecutar el desarrollo mediante GitHub Spec-Kit;
- implementar capacidades completas de extremo a extremo;
- evitar arquitectura especulativa;
- impedir que las pruebas de una fase dependan de comportamiento futuro;
- mantener el sistema compilable, ejecutable y verificable después de cada fase;
- disponer de criterios objetivos para determinar cuándo una fase está completa;
- registrar el avance real del proyecto;
- mantener trazabilidad entre requisitos, implementación y pruebas.

El roadmap no reemplaza la SRS.

La SRS define **qué debe hacer el sistema**.

Este roadmap define **en qué orden debe construirse**.

---

# 2. Principio rector de implementación

La implementación seguirá la siguiente regla:

> Cada fase deberá entregar una capacidad vertical completa, observable y verificable utilizando exclusivamente capacidades existentes en esa fase o en fases anteriores.

Por lo tanto:

> Ninguna prueba de una fase podrá requerir comportamiento, entidades, tablas, servicios, puertos, mocks o infraestructura perteneciente exclusivamente a una fase posterior.

Se prohíbe anticipar lógica futura únicamente con el argumento de que será necesaria más adelante.

---

# 3. Reglas obligatorias para Spec-Kit

## RD-001 — Dependencia unidireccional entre fases

Una fase podrá depender de:

- infraestructura base ya implementada;
- comportamiento de fases anteriores;
- contratos ya consolidados.

Una fase no podrá depender de:

- endpoints futuros;
- entidades futuras;
- repositorios futuros;
- servicios futuros;
- integraciones futuras;
- mocks que simulen capacidades todavía inexistentes.

La dependencia permitida es:

```text
Phase N
   |
   +-- puede depender de --> Phase 1 .. Phase N-1
   |
   +-- NO puede depender --> Phase N+1 .. Phase 8
```

---

## RD-002 — No arquitectura especulativa

No deberán crearse anticipadamente abstracciones cuya única justificación sea una fase posterior.

Ejemplos prohibidos:

```text
IRefreshTokenService     durante Phase 1
IEmailSender             durante Phase 1
IPasswordRecoveryService durante Phase 2
ISessionRepository       durante Phase 3
IKeyRotationService      en cualquier fase del MVP
```

Una abstracción deberá aparecer cuando exista una necesidad funcional real en la fase actual.

---

## RD-003 — Estado saludable obligatorio

Al finalizar cada fase:

```text
build        PASS
tests        PASS
startup      PASS
feature      PASS
regression   PASS
```

No se considerará terminada una fase si deja pruebas rotas con la expectativa de que una fase futura las repare.

---

## RD-004 — Evolución de reglas existentes

Una fase posterior podrá ampliar el comportamiento de una regla anterior.

Ejemplo:

```text
Phase 3
-------
Disable user
    -> impide login


Phase 4
-------
Disable user
    -> impide login
    -> revoca refresh sessions
```

La Fase 3 no deberá crear infraestructura de refresh tokens para satisfacer anticipadamente el comportamiento de Fase 4.

---

## RD-005 — Pruebas contra comportamiento existente

Las pruebas deberán verificar exclusivamente comportamiento implementado.

Está prohibido:

- escribir tests `Skip` esperando fases posteriores;
- introducir mocks de servicios aún inexistentes;
- construir fixtures con entidades que todavía no forman parte del dominio;
- utilizar TODO tests como criterio de completitud.

Los tests de fases futuras deberán incorporarse cuando comience la fase correspondiente.

---

## RD-006 — No breaking regressions

Una fase posterior podrá extender comportamiento, pero no deberá romper contratos consolidados de fases anteriores salvo modificación explícita de la SRS.

---

## RD-007 — Commit de cierre

Cada fase deberá finalizar con:

- todos los tests PASS;
- checklist de fase actualizado;
- documentación de decisiones relevantes;
- commit identificable de cierre.

Formato recomendado:

```text
phase-N: complete <capability>
```

Ejemplo:

```text
phase-4: complete refresh sessions and logout
```


## RD-008 — Persistencia independiente de Compose

Los datos y materiales criptográficos persistentes de producción no deberán depender del ciclo de vida de los volúmenes administrados por el propio proyecto Docker Compose.

Como mínimo, deberán sobrevivir a `docker compose down -v`:

- SQLite;
- key ring de ASP.NET Core Data Protection;
- clave privada RSA de firma.

La opción de referencia será utilizar bind mounts hacia paths explícitos del host. Se permitirá un volumen Docker `external` cuando su ciclo de vida sea administrado fuera del proyecto Compose.

Esta regla no agrega servicios al despliegue.

---

# 4. Estados de seguimiento

Utilizar los siguientes estados:

| Símbolo | Estado | Significado |
|---|---|---|
| `[ ]` | Pending | No iniciado |
| `[~]` | In Progress | En implementación |
| `[x]` | Complete | Finalizado y validado |
| `[!]` | Blocked | Bloqueado |
| `[-]` | Not Applicable | No aplica |

Una fase sólo podrá marcarse `[x]` cuando se hayan cumplido todos sus criterios de salida.

---

# 5. Dashboard general

| Fase | Capacidad | Estado | Dependencia | Gate |
|---|---|---|---|---|
| 1 | Bootstrap + Identity + Admin + Login + JWT | `[x]` | Ninguna | `G1` |
| 2 | Validación JWT en APIs consumidoras | `[x]` | Phase 1 | `G2` |
| 3 | Administración de usuarios y roles | `[x]` | Phase 1-2 | `G3` |
| 4 | Refresh tokens + sesiones + logout | `[ ]` | Phase 1-3 | `G4` |
| 5 | Cambio de contraseña | `[ ]` | Phase 1-4 | `G5` |
| 6 | Recuperación de contraseña + email | `[ ]` | Phase 1-5 | `G6` |
| 7 | Security hardening | `[ ]` | Phase 1-6 | `G7` |
| 8 | Operación + integración + validación final | `[ ]` | Phase 1-7 | `G8` |

---

# 6. Dependency graph

```text
Phase 1
Bootstrap + Identity + Admin + Login + JWT
        |
        v
Phase 2
JWT validation in APIs
        |
        v
Phase 3
Users + Roles
        |
        v
Phase 4
Refresh + Sessions + Logout
        |
        v
Phase 5
Change Password
        |
        v
Phase 6
Forgot / Reset Password + Email
        |
        v
Phase 7
Security Hardening
        |
        v
Phase 8
Operations + Final Integration
```

No existen dependencias hacia adelante.

---

# 7. Phase 1 — Bootstrap + Identity + Admin + Login + JWT

## 7.1 Objetivo

Entregar la primera versión funcional completa de Authentication API.

Después de esta fase deberá ser posible:

```text
docker compose up
        |
        v
Auth API inicia
        |
        v
SQLite queda preparada
        |
        v
admin inicial existe
        |
        v
login email/password
        |
        v
JWT RS256 válido
```

Esta fase establece la plataforma mínima sobre la cual crecerán todas las demás.

---

## 7.2 Alcance

### Infraestructura de aplicación

- [ ] Crear solución .NET.
- [ ] Crear proyecto Authentication API.
- [ ] Configurar ASP.NET Core.
- [ ] Configurar Entity Framework Core.
- [ ] Configurar SQLite.
- [ ] Configurar ASP.NET Core Identity.
- [ ] Configurar Dockerfile.
- [ ] Configurar integración mínima con Docker Compose.
- [ ] Configurar almacenamiento persistente SQLite independiente del ciclo de vida del proyecto Compose.
- [ ] Utilizar como referencia un bind mount hacia un directorio explícito del host para SQLite.
- [ ] Permitir volumen `external` como alternativa administrada fuera del proyecto Compose.
- [ ] Documentar la ubicación persistente de SQLite.

### Inicialización de base

- [ ] Authentication API prepara automáticamente la base durante startup.
- [ ] No existe migration container.
- [ ] Compose no ejecuta `dotnet ef database update`.
- [ ] No existe script externo de bootstrap.
- [ ] Aplicar migraciones embebidas pendientes desde la aplicación.
- [ ] Startup es idempotente.
- [ ] Fallo de migración impide readiness.

### Identity bootstrap

- [ ] Crear rol `Administrator` cuando no exista.
- [ ] Crear administrador inicial cuando no exista.
- [ ] `UserName = admin`.
- [ ] `Email = admin@local.invalid`.
- [ ] Password inicial `admin`.
- [ ] Asignar `Administrator`.
- [ ] Reinicio no duplica usuario.
- [ ] Reinicio no duplica rol.
- [ ] Reinicio no sobrescribe cambios del usuario.

### Login

- [ ] Implementar `POST /api/auth/login`.
- [ ] Login por email.
- [ ] Validación mediante Identity.
- [ ] Usuario inexistente devuelve `401`.
- [ ] Password inválida devuelve `401`.
- [ ] Usuario deshabilitado queda preparado como estado de identidad si resulta necesario para Phase 3.
- [ ] Activar contabilización de fallos para lockout.
- [ ] Respuesta externa genérica.

### JWT

- [ ] Configurar RS256.
- [ ] Cargar clave privada desde configuración externa.
- [ ] Montar la clave privada RSA desde almacenamiento independiente del ciclo de vida del proyecto Compose.
- [ ] Evitar named volumes administrados por el propio Compose como única copia de la clave privada.
- [ ] Configurar permisos restrictivos y montaje de sólo lectura cuando la plataforma lo permita.
- [ ] Emitir access token.
- [ ] Incluir `sub`.
- [ ] Incluir `email`.
- [ ] Incluir `role`.
- [ ] Incluir `iss`.
- [ ] Incluir `aud`.
- [ ] Incluir `iat`.
- [ ] Incluir `exp`.
- [ ] Incluir `jti`.
- [ ] Duración configurable.
- [ ] Valor inicial recomendado: 15 minutos.

### Health

- [ ] Implementar `/health/live`.
- [ ] Implementar `/health/ready`.
- [ ] Readiness comprueba disponibilidad de SQLite.
- [ ] Readiness falla si startup de DB falla.

---

## 7.3 Fuera de alcance de Phase 1

No implementar:

- [ ] Refresh tokens.
- [ ] Logout real basado en sesiones.
- [ ] CRUD administrativo.
- [ ] Recuperación de contraseña.
- [ ] Envío de correo.
- [ ] Password reset.
- [ ] Gestión completa de rate limiting.
- [ ] Cookie de refresh.
- [ ] Session family.
- [ ] JWKS.
- [ ] Rotación de claves.
- [ ] Blacklist JWT.

---

## 7.4 Pruebas mínimas

### Startup

- [ ] Base vacía inicia correctamente.
- [ ] Schema se crea/aplica automáticamente.
- [ ] Base existente inicia correctamente.
- [ ] Reinicio es idempotente.

### Admin bootstrap

- [ ] Se crea `Administrator`.
- [ ] Se crea `admin`.
- [ ] `admin@local.invalid` existe.
- [ ] Password inicial `admin` autentica.
- [ ] Segundo startup no duplica admin.

### Login

- [ ] Login válido.
- [ ] Password incorrecta.
- [ ] Email inexistente.
- [ ] Respuestas externas equivalentes.
- [ ] Fallos contabilizan para Identity lockout.

### JWT

- [ ] Token firmado con RS256.
- [ ] Claims obligatorios.
- [ ] Issuer correcto.
- [ ] Audience correcto.
- [ ] Expiración correcta.
- [ ] Clave privada no aparece en responses/logs.

### Health

- [ ] Liveness saludable.
- [ ] Readiness saludable con DB disponible.
- [ ] Readiness falla ante error de inicialización.

### Persistencia de despliegue

- [ ] SQLite no reside únicamente en la capa efímera del contenedor.
- [ ] SQLite no depende exclusivamente de un named volume administrado por el proyecto Compose.
- [ ] La clave privada RSA no depende exclusivamente de un named volume administrado por el proyecto Compose.
- [ ] Las rutas persistentes quedan documentadas.

---

## 7.5 Criterio de salida — Gate G1

Phase 1 podrá marcarse completa cuando:

- [x] `docker compose up` permite iniciar Auth API desde almacenamiento vacío.
- [x] No existe etapa externa de migración.
- [x] Admin inicial existe.
- [x] Login funciona.
- [x] Se obtiene JWT RS256 válido.
- [x] Build PASS.
- [x] Tests PASS.
- [x] Restart PASS.
- [x] Persistencia SQLite configurada fuera del ciclo de vida del proyecto Compose.
- [x] Clave RSA persistente configurada fuera del ciclo de vida del proyecto Compose.
- [x] Checklist actualizado.
- [x] Commit de cierre creado.

---

# 8. Phase 2 — Validación JWT en APIs consumidoras

## 8.1 Objetivo

Demostrar tempranamente que el modelo de autenticación centralizada funciona realmente entre servicios.

El resultado esperado:

```text
Auth API
   |
   +-- JWT --> API A --> validación local
   |
   +-- JWT --> API B --> validación local
```

Authentication API no participa en cada request de negocio.

---

## 8.2 Alcance

### Material público

- [ ] Distribuir/configurar clave pública en API A.
- [ ] Distribuir/configurar clave pública en API B.
- [ ] Garantizar que ninguna reciba clave privada.

### JWT Bearer

En ambas APIs:

- [ ] Configurar JWT Bearer.
- [ ] Validar algoritmo esperado.
- [ ] Validar firma.
- [ ] Validar issuer.
- [ ] Validar audience.
- [ ] Validar expiración.
- [ ] Mapear `sub`.
- [ ] Mapear `role`.

### Autorización mínima

- [ ] Proteger al menos un endpoint de prueba/real en API A.
- [ ] Proteger al menos un endpoint de prueba/real en API B.
- [ ] Diferenciar `401` de `403`.

---

## 8.3 Fuera de alcance de Phase 2

No implementar:

- [ ] Refresh.
- [ ] Usuarios administrativos.
- [ ] Roles dinámicos.
- [ ] Revocación.
- [ ] Introspection.
- [ ] JWKS.
- [ ] Validación remota de tokens.
- [ ] Blacklist.

---

## 8.4 Pruebas mínimas

- [ ] JWT válido permite acceso en API A.
- [ ] JWT válido permite acceso en API B.
- [ ] Request sin JWT -> `401`.
- [ ] Firma inválida -> `401`.
- [ ] Token expirado -> `401`.
- [ ] Issuer inválido -> `401`.
- [ ] Audience inválido -> `401`.
- [ ] Rol insuficiente -> `403`.
- [ ] API A funciona sin consultar Auth API por request.
- [ ] API B funciona sin consultar Auth API por request.

---

## 8.5 Criterio de salida — Gate G2

- [x] Ambas APIs validan JWT localmente.
- [x] Ambas APIs rechazan tokens incorrectos.
- [x] Autorización por rol demostrada.
- [x] Auth API puede estar temporalmente indisponible y un JWT vigente sigue siendo validable.
- [x] Build PASS.
- [x] Tests PASS.
- [x] Regression Phase 1 PASS.
- [x] Commit de cierre creado.

---

# 9. Phase 3 — Administración de usuarios y roles

## 9.1 Objetivo

Incorporar administración básica de identidades y RBAC sin introducir todavía sesiones renovables.

---

## 9.2 Alcance

### Usuarios

- [ ] `GET /api/admin/users`.
- [ ] `GET /api/admin/users/{id}`.
- [ ] `POST /api/admin/users`.
- [ ] `PATCH /api/admin/users/{id}`.
- [ ] `POST /api/admin/users/{id}/enable`.
- [ ] `POST /api/admin/users/{id}/disable`.

### Roles

- [ ] `GET /api/admin/roles`.
- [ ] `POST /api/admin/roles`.
- [ ] `PATCH /api/admin/roles/{id}`.
- [ ] `DELETE /api/admin/roles/{id}`.
- [ ] `PUT /api/admin/users/{id}/roles`.

### Autorización

- [ ] Proteger `/api/admin/*`.
- [ ] Requerir `Administrator`.
- [ ] `401` cuando no está autenticado.
- [ ] `403` cuando está autenticado sin rol.

### Reglas de dominio

- [ ] Email único normalizado.
- [ ] Password cumple política Identity.
- [ ] Roles únicos.
- [ ] No eliminar rol con usuarios asignados.
- [ ] No deshabilitar último administrador habilitado.
- [ ] No retirar rol Administrator al último administrador habilitado.

### Disable user

En esta fase:

```text
disable
   |
   +--> impide nuevos logins
```

No existen aún refresh sessions.

---

## 9.3 Fuera de alcance de Phase 3

No implementar:

- [ ] RefreshToken.
- [ ] SessionFamily.
- [ ] Revoke sessions.
- [ ] Logout stateful.
- [ ] Email recovery.
- [ ] Password reset.

No crear interfaces ficticias para estas capacidades.

---

## 9.4 Pruebas mínimas

### Users

- [ ] Crear usuario.
- [ ] Email duplicado rechazado.
- [ ] Listar usuarios.
- [ ] Consultar usuario.
- [ ] Modificación válida.
- [ ] Deshabilitar.
- [ ] Usuario deshabilitado no puede login.
- [ ] Rehabilitar.
- [ ] Usuario rehabilitado puede volver a login.

### Roles

- [ ] Crear rol.
- [ ] Rol duplicado rechazado.
- [ ] Asignar rol.
- [ ] Retirar rol.
- [ ] Eliminar rol no asignado.
- [ ] Rechazar eliminación de rol asignado.

### Protección administrativa

- [ ] Anónimo -> `401`.
- [ ] Usuario normal -> `403`.
- [ ] Administrator -> permitido.
- [ ] Último administrador no puede deshabilitarse.
- [ ] Último administrador no puede perder rol.

---

## 9.5 Criterio de salida — Gate G3

- [x] CRUD administrativo mínimo operativo.
- [x] RBAC operativo.
- [x] Regla del último administrador protegida.
- [x] Disable afecta login.
- [x] Ninguna infraestructura de refresh fue anticipada.
- [x] Build PASS.
- [x] Tests PASS.
- [x] Regression Phase 1-2 PASS.
- [x] Commit de cierre creado.

---

# 10. Phase 4 — Refresh tokens + sesiones + logout

## 10.1 Objetivo

Agregar sesiones renovables seguras sin alterar el modelo stateless de validación de access tokens.

---

## 10.2 Nuevas capacidades de dominio

Recién en esta fase se introducen:

```text
RefreshToken
FamilyId
TokenHash
CreatedAtUtc
ExpiresAtUtc
RevokedAtUtc
ReplacedByTokenId
```

- [ ] Definir modelo de refresh token.
- [ ] Persistir hash, nunca token completo.
- [ ] Definir familia de tokens.
- [ ] Definir expiración absoluta.
- [ ] Definir revocación.

---

## 10.3 Endpoints

- [ ] `POST /api/auth/refresh`.
- [ ] `POST /api/auth/logout`.
- [ ] `POST /api/admin/users/{id}/revoke-sessions`.

---

## 10.4 Login extendido

Phase 1 login deberá ampliarse:

```text
login exitoso
     |
     +--> access token
     |
     +--> refresh session
```

- [ ] Login crea refresh token.
- [ ] Login crea FamilyId.
- [ ] Refresh token se entrega al cliente.
- [ ] Persistir solamente hash.

---

## 10.5 Refresh rotation

- [ ] Validar token.
- [ ] Validar expiración.
- [ ] Validar estado del usuario.
- [ ] Revocar token presentado.
- [ ] Crear nuevo token.
- [ ] Mantener FamilyId.
- [ ] Emitir nuevo access token.

---

## 10.6 Replay detection

- [ ] Detectar reutilización de token rotado.
- [ ] Revocar familia completa.
- [ ] Registrar evento de seguridad.

---

## 10.7 Logout

- [ ] Revocar sesión/familia correspondiente.
- [ ] Operación idempotente.
- [ ] No introducir blacklist de JWT.
- [ ] Access token vigente expira naturalmente.

---

## 10.8 Extensión de comportamiento anterior

A partir de esta fase:

```text
disable user
    |
    +--> impide login
    +--> revoca refresh sessions
```

- [ ] Extender disable para revocar sesiones.
- [ ] Enable no restaura sesiones revocadas.

---

## 10.9 Pruebas mínimas

- [ ] Login crea sesión.
- [ ] Refresh válido.
- [ ] Refresh rota token.
- [ ] Token anterior deja de funcionar.
- [ ] Refresh expirado rechazado.
- [ ] Token desconocido rechazado.
- [ ] Reuse detectado.
- [ ] Reuse revoca familia.
- [ ] Usuario disabled no puede refresh.
- [ ] Disable revoca sesiones.
- [ ] Logout revoca sesión.
- [ ] Logout idempotente.
- [ ] Revoke-sessions administrativo.
- [ ] Access token previo no requiere blacklist.

---

## 10.10 Criterio de salida — Gate G4

- [ ] Login + refresh + logout forman un ciclo completo.
- [ ] Rotación funciona.
- [ ] Reuse detection funciona.
- [ ] Disable revoca sesiones.
- [ ] API A/B continúan validando JWT localmente.
- [ ] Build PASS.
- [ ] Tests PASS.
- [ ] Regression Phase 1-3 PASS.
- [ ] Commit de cierre creado.

---

# 11. Phase 5 — Cambio de contraseña

## 11.1 Objetivo

Permitir que cualquier usuario autenticado cambie su contraseña, cerrando especialmente el ciclo de seguridad del administrador inicial `admin/admin`.

---

## 11.2 Alcance

Endpoint:

```text
POST /api/auth/change-password
```

- [ ] Requiere autenticación.
- [ ] Requiere password actual.
- [ ] Requiere nueva password.
- [ ] Validar password actual mediante Identity.
- [ ] Aplicar política de password.
- [ ] Actualizar password mediante Identity.
- [ ] Revocar otras sesiones renovables.
- [ ] No volver a establecer `admin` automáticamente.

---

## 11.3 Administrador inicial

Flujo requerido:

```text
admin@local.invalid
password: admin
        |
        v
change-password
        |
        v
new secret
        |
        v
restart
        |
        v
new secret remains
```

---

## 11.4 Fuera de alcance

No introducir todavía:

- [ ] SMTP.
- [ ] `IEmailSender`.
- [ ] Forgot password.
- [ ] Reset token.

---

## 11.5 Pruebas mínimas

- [ ] Endpoint exige JWT.
- [ ] Password actual incorrecta rechazada.
- [ ] Nueva password inválida rechazada.
- [ ] Cambio exitoso.
- [ ] Password anterior deja de autenticar.
- [ ] Password nueva autentica.
- [ ] Sesiones anteriores revocadas.
- [ ] Restart no restaura `admin`.
- [ ] Admin puede cambiar password sin email funcional externo.

---

## 11.6 Criterio de salida — Gate G5

- [ ] Change password completo.
- [ ] Admin inicial puede abandonar credencial por defecto.
- [ ] Revocación de sesiones integrada.
- [ ] Sin infraestructura de email anticipada.
- [ ] Build PASS.
- [ ] Tests PASS.
- [ ] Regression Phase 1-4 PASS.
- [ ] Commit de cierre creado.

---

# 12. Phase 6 — Recuperación de contraseña + email

## 12.1 Objetivo

Agregar recuperación de contraseña sólo cuando el resto del ciclo de autenticación ya existe.

Esta es la primera fase en la que el envío de correo es una dependencia funcional real.

---

## 12.2 Infraestructura nueva permitida

En esta fase podrá introducirse:

```text
IEmailSender
SMTP implementation
Password reset token delivery
```

- [ ] Crear abstracción mínima de envío de correo.
- [ ] Implementación SMTP.
- [ ] Configuración externa.
- [ ] No registrar secretos SMTP.
- [ ] No registrar reset token.

---

## 12.3 Data Protection

- [ ] Persistir Data Protection keys.
- [ ] Utilizar almacenamiento independiente del ciclo de vida del proyecto Compose.
- [ ] Utilizar bind mount de host como opción de referencia o volumen `external` como alternativa.
- [ ] Verificar supervivencia a restart.
- [ ] Verificar que la configuración no dependa de named volumes administrados por el propio Compose.
- [ ] No agregar servicio externo.

---

## 12.4 Forgot password

Endpoint:

```text
POST /api/auth/forgot-password
```

- [ ] Acceso anónimo.
- [ ] Recibe email.
- [ ] Respuesta genérica.
- [ ] No revela existencia de usuario.
- [ ] Generar token mediante Identity.
- [ ] Solicitar envío de correo.

---

## 12.5 Reset password

Endpoint:

```text
POST /api/auth/reset-password
```

- [ ] Recibe email.
- [ ] Recibe reset token.
- [ ] Recibe nueva password.
- [ ] Validar token.
- [ ] Validar password.
- [ ] Cambiar password.
- [ ] Revocar todas las sesiones renovables.

---

## 12.6 Pruebas mínimas

- [ ] Forgot para usuario existente.
- [ ] Forgot para usuario inexistente.
- [ ] Misma respuesta externa.
- [ ] Email sender invocado sólo cuando corresponde.
- [ ] Reset token válido.
- [ ] Reset token inválido.
- [ ] Reset token alterado.
- [ ] Reset token vencido cuando pueda probarse.
- [ ] Reset cambia password.
- [ ] Reset revoca sesiones.
- [ ] Restart no invalida innecesariamente token aún válido.
- [ ] El key ring de Data Protection está configurado fuera del ciclo de vida del proyecto Compose.
- [ ] Reset tokens no aparecen en logs.

---

## 12.7 Criterio de salida — Gate G6

- [ ] Forgot/reset completo.
- [ ] SMTP desacoplado.
- [ ] Data Protection persistente.
- [ ] Anti-enumeración funcional.
- [ ] Reset revoca sesiones.
- [ ] Build PASS.
- [ ] Tests PASS.
- [ ] Regression Phase 1-5 PASS.
- [ ] Commit de cierre creado.

---

# 13. Phase 7 — Security hardening

## 13.1 Objetivo

Aplicar protección transversal a la superficie de autenticación completa.

Se realiza en esta etapa porque ahora ya existen todos los endpoints sensibles.

---

## 13.2 Identity lockout

- [ ] `MaxFailedAccessAttempts = 5` inicial.
- [ ] `DefaultLockoutTimeSpan = 15 min` inicial.
- [ ] Configurable externamente.
- [ ] Login contabiliza fallos.
- [ ] Respuesta no revela lockout.

---

## 13.3 Application rate limiting

Políticas independientes para:

- [ ] `/login`.
- [ ] `/refresh`.
- [ ] `/forgot-password`.
- [ ] `/reset-password`.

Requisitos:

- [ ] Rate limiting por IP.
- [ ] Configurable.
- [ ] `429` al exceder límite.
- [ ] Forgot puede limitar además por email normalizado.

---

## 13.4 Reverse proxy

- [ ] Primera capa de rate limiting.
- [ ] Forwarded headers.
- [ ] Known proxies/networks.
- [ ] No confiar en forwarded headers arbitrarios.
- [ ] Verificar IP real de origen.

---

## 13.5 Refresh cookie

- [ ] `HttpOnly`.
- [ ] `Secure` en producción.
- [ ] `SameSite` restrictivo.
- [ ] Path limitado cuando corresponda.
- [ ] Eliminar/inutilizar cookie en logout.
- [ ] Access token preferentemente en memoria.

---

## 13.6 CSRF / origin controls

- [ ] Refresh protegido frente a cross-site request indebido.
- [ ] Logout protegido.
- [ ] Validación de origen cuando corresponda.
- [ ] CORS no se habilita si same-origin lo hace innecesario.
- [ ] Si CORS es necesario, allowlist explícita.

---

## 13.7 Anti-enumeration

Comprobar equivalencia externa para:

```text
unknown account
wrong password
locked account
disabled account
```

- [ ] Códigos equivalentes.
- [ ] Mensajes equivalentes.
- [ ] Timing razonablemente comparable.
- [ ] Forgot no enumera.
- [ ] Reset no revela información innecesaria.

---

## 13.8 Protección de secretos

- [ ] Passwords fuera de logs.
- [ ] Access tokens completos fuera de logs.
- [ ] Refresh tokens fuera de logs.
- [ ] Reset tokens fuera de logs.
- [ ] RSA private key fuera de logs.
- [ ] Secretos de email fuera de logs.

---

## 13.9 Pruebas mínimas

- [ ] 5 fallos bloquean cuenta.
- [ ] Lockout expira.
- [ ] Rate limit login.
- [ ] Rate limit refresh.
- [ ] Rate limit forgot.
- [ ] Rate limit reset.
- [ ] Lockout por cuenta y rate limiting por IP son independientes.
- [ ] `429` correcto.
- [ ] Forwarded header spoofing no altera origen confiable.
- [ ] Refresh cookie tiene flags requeridos.
- [ ] Logout limpia cookie.
- [ ] Request cross-origin indebido rechazado cuando corresponda.
- [ ] Anti-enumeration responses.
- [ ] Secret scan/log assertions.

---

## 13.10 Criterio de salida — Gate G7

- [ ] Identity lockout completo.
- [ ] Rate limiting completo.
- [ ] Proxy trust configurado.
- [ ] Cookie segura.
- [ ] CSRF/origin cubierto.
- [ ] Anti-enumeration cubierto.
- [ ] Logs sin secretos.
- [ ] Build PASS.
- [ ] Tests PASS.
- [ ] Regression Phase 1-6 PASS.
- [ ] Commit de cierre creado.

---

# 14. Phase 8 — Operación, integración y validación final

## 14.1 Objetivo

Cerrar el producto como unidad desplegable y operable, validando la SRS completa sin incorporar nuevas capacidades funcionales.

Esta fase no deberá convertirse en una fase de features pendientes.

Si una feature funcional de fases anteriores falta, deberá corregirse en su fase conceptual antes de declarar cierre.

---

## 14.2 Logging y observabilidad

- [ ] Logging estructurado.
- [ ] Correlation ID.
- [ ] Login success.
- [ ] Login failure.
- [ ] Lockout.
- [ ] Rate limit.
- [ ] Logout.
- [ ] Password change.
- [ ] Password reset.
- [ ] User create.
- [ ] User disable/enable.
- [ ] Role assign/remove.
- [ ] Refresh reuse detected.
- [ ] Sessions revoked.

---

## 14.3 OpenAPI

- [ ] Contrato OpenAPI generado.
- [ ] Requests documentados.
- [ ] Responses documentadas.
- [ ] Auth Bearer documentada.
- [ ] Documentation UI limitada según ambiente.

---

## 14.4 Docker Compose final

Servicios funcionales:

```text
frontend
auth-api
api-a
api-b
```

Validar:

- [ ] No migration container.
- [ ] No bootstrap container.
- [ ] No Redis.
- [ ] No Vault.
- [ ] No servicio Identity externo.
- [ ] Frontend/reverse proxy funciona.
- [ ] El navegador enruta `/auth/*`, `/api-a/*` y `/api-b/*` exclusivamente a través del punto de entrada del frontend/reverse proxy.
- [ ] Auth API sin puerto público directo en producción.
- [ ] API A y API B sin exposición directa necesaria para el navegador.
- [ ] Internal network correcta.

---

## 14.5 Persistencia y resiliencia del almacenamiento

Validar persistencia de:

- [ ] SQLite.
- [ ] Identity users.
- [ ] Roles.
- [ ] Refresh sessions.
- [ ] Data Protection keys.
- [ ] RSA signing key.

Validar además:

- [ ] SQLite reside en almacenamiento independiente del ciclo de vida del proyecto Compose.
- [ ] Data Protection reside en almacenamiento independiente del ciclo de vida del proyecto Compose.
- [ ] RSA private key reside en almacenamiento independiente del ciclo de vida del proyecto Compose.
- [ ] Las rutas persistentes están documentadas.
- [ ] La eliminación de datos requiere una acción explícita sobre el almacenamiento persistente.

---

## 14.6 Restart / rebuild / teardown validation

Validar primero el ciclo ordinario de restart/rebuild, comprobando que:

- [ ] DB permanece.
- [ ] Usuarios permanecen.
- [ ] Roles permanecen.
- [ ] Password modificada del admin permanece.
- [ ] Refresh/session state esperado permanece.
- [ ] Data Protection permanece.
- [ ] Signing key permanece.
- [ ] API A sigue validando.
- [ ] API B sigue validando.

En un entorno descartable equivalente a producción, validar explícitamente el escenario destructivo para volúmenes administrados por Compose:

- [ ] Ejecutar `docker compose down -v`.
- [ ] Verificar que SQLite continúa existiendo en el almacenamiento persistente externo al Compose.
- [ ] Verificar que el key ring de Data Protection continúa existiendo.
- [ ] Verificar que la clave privada RSA continúa existiendo.
- [ ] Ejecutar nuevamente `docker compose up -d`.
- [ ] Verificar que usuarios y roles continúan disponibles.
- [ ] Verificar que la password modificada del administrador continúa vigente.
- [ ] Verificar que API A y API B pueden seguir validando tokens emitidos con la clave persistida.

---

## 14.7 Backup y restauración de SQLite

- [ ] Documentar procedimiento de backup consistente de SQLite.
- [ ] El procedimiento no requiere un servicio permanente adicional.
- [ ] Evitar copias inconsistentes durante escrituras concurrentes.
- [ ] Documentar procedimiento de restauración.
- [ ] Crear un backup de prueba.
- [ ] Restaurarlo en un entorno descartable.
- [ ] Iniciar Authentication API contra la base restaurada.
- [ ] Verificar usuarios.
- [ ] Verificar roles.
- [ ] Verificar autenticación de al menos una cuenta conocida.

---

## 14.8 End-to-end acceptance flow

- [ ] Deploy desde almacenamiento vacío.
- [ ] Admin automático.
- [ ] Login admin.
- [ ] Cambiar password admin.
- [ ] Crear usuario.
- [ ] Crear/asignar rol.
- [ ] Login usuario.
- [ ] Acceso API A.
- [ ] Acceso API B.
- [ ] Refresh.
- [ ] Rotation.
- [ ] Logout.
- [ ] Re-login.
- [ ] Forgot password.
- [ ] Reset password.
- [ ] Sesiones anteriores revocadas.
- [ ] Disable user.
- [ ] Login rechazado.
- [ ] Rate limit demostrado.
- [ ] Lockout demostrado.
- [ ] `docker compose down -v` no destruye SQLite, Data Protection ni clave RSA.
- [ ] Backup/restore SQLite demostrado.
- [ ] Acceso externo únicamente a través del reverse proxy.

---

## 14.9 Criterio de salida — Gate G8

La implementación completa podrá considerarse terminada cuando:

- [ ] Todos los requisitos MVP de la SRS estén implementados.
- [ ] Todos los gates G1-G7 permanezcan PASS.
- [ ] Compose final sea autocontenido.
- [ ] No existan pasos manuales de migración.
- [ ] No exista bootstrap externo.
- [ ] Persistencia crítica sea independiente del ciclo de vida de Compose.
- [ ] `docker compose down -v` survival PASS en entorno de aceptación.
- [ ] SQLite backup PASS.
- [ ] SQLite restore PASS.
- [ ] Reverse proxy routing PASS.
- [ ] E2E acceptance PASS.
- [ ] Tests completos PASS.
- [ ] Build PASS.
- [ ] Logs revisados.
- [ ] OpenAPI revisado.
- [ ] Documentación de operación actualizada.
- [ ] Commit/tag de cierre creado.

---

# 15. Matriz de crecimiento vertical

| Fase | Entrada observable | Dominio nuevo | Persistencia nueva | Salida observable |
|---|---|---|---|---|
| 1 | Email/password | Identity básica | Identity + SQLite | JWT |
| 2 | JWT | Ninguno relevante | Ninguna | APIs protegidas |
| 3 | Operación admin | User/Role rules | Identity existente | Usuarios/roles gestionables |
| 4 | Refresh credential | Session/Token Family | RefreshTokens | Sesión renovable |
| 5 | Current/new password | Password change rules | Identity + sessions | Password cambiada |
| 6 | Email/reset token | Recovery workflow | Data Protection | Password recuperable |
| 7 | Requests hostiles | Security policies | Config/state mínimo | Superficie endurecida |
| 8 | Deployment completo | Ninguno nuevo | Ninguna funcional nueva | Producto aceptado + resiliencia/restore verificados |

Esta tabla sirve como control contra crecimiento horizontal prematuro.

---

# 16. Matriz de "no inventar futuro"

| Si estamos en... | NO introducir todavía |
|---|---|
| Phase 1 | RefreshToken, SessionFamily, EmailSender, ResetPasswordService |
| Phase 2 | CRUD users, sessions, mail |
| Phase 3 | Refresh repositories, revoke-session mocks |
| Phase 4 | SMTP, password reset tokens |
| Phase 5 | EmailSender, forgot-password workflows |
| Phase 6 | Políticas sofisticadas de proxy no necesarias para recovery |
| Phase 7 | Nuevas features de producto |
| Phase 8 | Features nuevas; sólo cierre e integración |

---

# 17. Estrategia recomendada de Spec-Kit por fase

Cada fase podrá desarrollarse como una feature independiente de Spec-Kit.

Flujo recomendado:

```text
/speckit.specify
       |
       v
/speckit.clarify
       |
       v
/speckit.plan
       |
       v
/speckit.tasks
       |
       v
/speckit.analyze
       |
       v
/speckit.implement
       |
       v
tests + gate
       |
       v
phase complete
```

Antes de `/speckit.implement` deberá verificarse que `tasks.md`:

- [ ] no contenga tareas pertenecientes a fases futuras;
- [ ] no cree abstracciones especulativas;
- [ ] preserve las reglas de fases anteriores;
- [ ] tenga pruebas asociadas al comportamiento actual;
- [ ] permita cerrar la fase de extremo a extremo.

---

# 18. Checklist de análisis previo a cada implementación

Antes de implementar cada fase responder:

### Dependencias

- [ ] ¿Todas las dependencias provienen de fases anteriores?
- [ ] ¿Existe alguna tarea que presupone una feature futura?
- [ ] ¿Se está creando una interfaz para algo que todavía no existe?

### Dominio

- [ ] ¿Las reglas nuevas aparecen porque la capacidad actual las necesita?
- [ ] ¿Se está modificando una regla anterior de forma compatible?
- [ ] ¿Se evita modelar estados futuros innecesarios?

### Persistencia

- [ ] ¿Cada tabla nueva responde a comportamiento de esta fase?
- [ ] ¿Existe alguna columna agregada sólo "para después"?
- [ ] ¿La migración puede explicarse por requisitos actuales?

### Tests

- [ ] ¿Cada test puede pasar al finalizar esta fase?
- [ ] ¿Algún test necesita mocks de fases futuras?
- [ ] ¿Las pruebas anteriores siguen siendo válidas?
- [ ] ¿Hay pruebas negativas?

### Infraestructura

- [ ] ¿Se agregó sólo infraestructura necesaria?
- [ ] ¿Se evitó introducir servicios nuevos?
- [ ] ¿Docker Compose sigue simple?
- [ ] ¿Los datos críticos de esta fase sobreviven al ciclo de vida de los contenedores?
- [ ] ¿Algún dato crítico depende indebidamente de un named volume administrado por el proyecto Compose?

---

# 19. Definition of Done global

Una tarea individual sólo puede considerarse completa si:

- [ ] compila;
- [ ] cumple el requisito correspondiente;
- [ ] posee prueba cuando el comportamiento es automatizable;
- [ ] no rompe pruebas previas;
- [ ] no introduce secretos;
- [ ] no agrega alcance futuro innecesario.

Una fase sólo puede considerarse completa si:

- [ ] todas sus tareas están completas;
- [ ] todos sus tests pasan;
- [ ] todos los tests anteriores pasan;
- [ ] puede demostrarse el flujo vertical de la fase;
- [ ] documentación relevante está actualizada;
- [ ] la persistencia introducida por la fase cumple RD-008 cuando corresponda;
- [ ] no existen TODO funcionales necesarios para declararla operativa;
- [ ] el gate de fase está aprobado.

---

# 20. Registro de avance

Actualizar esta sección al finalizar cada sesión relevante.

| Fecha | Fase | Estado | Cambio principal | Tests | Commit | Observaciones |
|---|---|---|---|---|---|---|
| 2026-10-07 | Phase 1 | Complete — G1 approved | Bootstrap, Identity, admin inicial, login por email, JWT RS256, health live/ready y persistencia SQLite/RSA fuera del ciclo de vida de Compose | Build 0 warnings; 19/19 tests; `tests/acceptance/phase-1.sh` ALL PASS | Commit de cierre `[Phase 1] Close Gate G1` | Aprobación explícita de G1 por el responsable del proyecto el 2026-10-07; evidencia en `docs/phase-1-operations.md`; reemplazo de la contraseña `admin` pendiente de Phase 5 |
| 2026-10-08 | Phase 2 | Complete — G2 approved | Validación JWT local en API A y API B (consumidor de referencia `ReferenceConsumer.Api` desplegado como `api-a`/`api-b`), rechazo `401`, autorización por rol `403`, clave pública únicamente, disponibilidad con Auth API detenida | Build 0 warnings; 31/31 tests; `tests/acceptance/phase-2.sh` ALL PASS (incluye regresión `phase-1.sh`) | Commit de cierre `[Phase 2] Close Gate G2` | Aprobación explícita de G2 por el responsable del proyecto el 2026-10-08; evidencia en `docs/phase-2-operations.md`; autorizado por DEC-009 y la enmienda 1.1 de Technical Constraints §5.2; proxy, frontend y no exposición directa de backends permanecen en Phase 8 |
| 2026-10-08 | Phase 3 | Complete — G3 approved | Administración de usuarios y roles en `/api/admin/*` (11 operaciones), RBAC con política `Administrator` sobre JWT validado localmente, estado habilitado (`ApplicationUser.IsEnabled`) que impide nuevos logins con el `401` genérico, reemplazo completo del conjunto de roles, protección del último administrador habilitado y del rol `Administrator`, también bajo concurrencia | Build 0 warnings; 59/59 tests; `tests/acceptance/phase-3.sh` ALL PASS (incluye regresión `phase-2.sh` y `phase-1.sh`) | Commit de cierre `[Phase 3] Close Gate G3` | Aprobación explícita de G3 por el responsable del proyecto el 2026-10-08; evidencia en `docs/phase-3-operations.md`; revisión de convergencia sin hallazgos; nueva configuración requerida `Jwt:ClockSkewSeconds` en `auth-api`; disable sólo impide nuevos logins: revocación de sesiones y refresh permanecen en Phase 4 |
| — | Phase 4 | Pending | — | — | — | — |
| — | Phase 5 | Pending | — | — | — | — |
| — | Phase 6 | Pending | — | — | — | — |
| — | Phase 7 | Pending | — | — | — | — |
| — | Phase 8 | Pending | — | — | — | — |

---

# 21. Registro de decisiones

Documentar aquí decisiones que alteren el roadmap sin necesariamente modificar todavía la SRS.

| ID | Fecha | Fase | Decisión | Motivo | Impacto |
|---|---|---|---|---|---|
| DEC-001 | 2026-10-06 | Global | Implementación en 8 fases verticales | Evitar dependencias futuras y arquitectura especulativa | Define roadmap |
| DEC-002 | 2026-10-06 | Phase 1 | DB se inicializa desde Auth API | Evitar migration/bootstrap containers | Simplifica Compose |
| DEC-003 | 2026-10-06 | Phase 1 | Admin inicial `admin@local.invalid` / `admin` | Primer acceso simple preservando login por email | Requiere cambio posterior de password |
| DEC-004 | 2026-10-06 | Phase 1-2 | RS256 con un único par de claves | Separar capacidad de firmar de capacidad de validar | Clave pública en APIs |
| DEC-005 | 2026-10-06 | Global | Sin rotación automática/JWKS obligatorio | Mantener alcance simple | Rotación manual |
| DEC-006 | 2026-10-06 | Phase 4 | Sin blacklist de JWT | Mantener APIs consumidoras stateless | Ventana residual hasta `exp` |
| DEC-007 | 2026-10-06 | Global | Persistencia crítica fuera del ciclo de vida del proyecto Compose | Evitar pérdida accidental por `docker compose down -v` | Bind mounts de host como referencia; `external` permitido |
| DEC-008 | 2026-10-06 | Phase 8 | Backup/restore SQLite forma parte del gate final | La persistencia no sustituye una estrategia de recuperación | Restore probado antes de cierre |
| DEC-009 | 2026-10-07 | Phase 2 | Proyecto `ReferenceConsumer.Api` desplegado como `api-a` y `api-b` | Las APIs de negocio reales son repositorios separados sin código; Roadmap §8/G2 exige validación local en servicios ejecutables | Enmienda 1.1 de Technical Constraints §5.2; sin capas hexagonales ni referencias a `Authentication.*` |

---

# 22. Riesgos de ejecución

## RISK-001 — Especulación arquitectónica

**Riesgo:** Spec-Kit o el agente introduce componentes futuros.

**Control:**

- revisar `plan.md`;
- revisar `tasks.md`;
- aplicar RD-002;
- eliminar tareas no justificadas por fase actual.

---

## RISK-002 — Tests adelantados

**Riesgo:** una fase temprana falla porque un test presupone una feature posterior.

**Control:**

- aplicar RD-005;
- tests se incorporan en la fase de la capacidad;
- no usar mocks de futuro.

---

## RISK-003 — Fases demasiado grandes

**Riesgo:** una fase deja de ser vertical y se convierte en múltiples features simultáneas.

**Control:**

- respetar el alcance documentado;
- dividir internamente en tareas, no en capas horizontales;
- no mover features futuras hacia atrás.

---

## RISK-004 — Fase 8 como "cajón de pendientes"

**Riesgo:** features incompletas se difieren arbitrariamente a Phase 8.

**Control:**

Phase 8 no debe implementar funcionalidad que pertenezca conceptualmente a Phase 1-7.

Los defectos deberán corregirse en la fase conceptual correspondiente.


## RISK-005 — Pérdida de almacenamiento por teardown

**Riesgo:** SQLite o material criptográfico quedan almacenados únicamente en named volumes administrados por Compose y son eliminados por `docker compose down -v`.

**Control:**

- aplicar RD-008;
- usar bind mounts de host como referencia;
- permitir únicamente volúmenes `external` como alternativa;
- probar supervivencia en Phase 8;
- mantener backup/restore documentado y probado.

---

# 23. Indicadores de progreso

El progreso no deberá medirse únicamente por cantidad de tareas.

Se recomienda registrar:

| Métrica | Objetivo |
|---|---|
| Fases completas | 8/8 |
| Gates aprobados | 8/8 |
| Tests de fase PASS | 100% |
| Regression tests PASS | 100% |
| Requisitos críticos sin cobertura | 0 |
| Features futuras anticipadas | 0 |
| Servicios infra adicionales no previstos | 0 |
| Pasos manuales DB deployment | 0 |
| Datos críticos eliminados por `docker compose down -v` | 0 |
| Restore SQLite probado | 1 PASS antes de release |
| Backends expuestos directamente al navegador | 0 en producción |

---

# 24. Resumen ejecutivo del roadmap

```text
PHASE 1
Identity + DB + Admin + Login + JWT
              |
              v
PHASE 2
JWT validation in Business APIs
              |
              v
PHASE 3
Users + Roles
              |
              v
PHASE 4
Refresh + Sessions + Logout
              |
              v
PHASE 5
Change Password
              |
              v
PHASE 6
Forgot + Reset + Email
              |
              v
PHASE 7
Security Hardening
              |
              v
PHASE 8
Operations + E2E Acceptance
```

Cada fase:

```text
implements
    +
tests
    +
integrates
    +
closes
```

antes de comenzar la siguiente.

---

# 25. Trazabilidad específica con SRS v1.1

| Cambio SRS v1.1 | Fase responsable | Validación final |
|---|---|---|
| SQLite fuera del ciclo de vida de Compose | Phase 1 | Phase 8 `down -v` survival |
| RSA private key fuera del ciclo de vida de Compose | Phase 1 | Phase 8 `down -v` survival |
| Data Protection fuera del ciclo de vida de Compose | Phase 6 | Phase 8 `down -v` survival |
| Backup SQLite documentado | Phase 8 | Restore real en entorno descartable |
| Reverse proxy como único punto de entrada | Phase 2 / Phase 7 | Phase 8 routing E2E |
| Sin migration/bootstrap containers | Phase 1 | Phase 8 Compose inspection |

---

# 26. Estado actual

```text
Roadmap defined: YES
SRS baseline: v1.1
SRS available: YES
Implementation started: YES
Phase 1: COMPLETE (Gate G1 approved 2026-10-07)
Phase 2: COMPLETE (Gate G2 approved 2026-10-08)
Phase 3: COMPLETE (Gate G3 approved 2026-10-08)
Current phase: Phase 4
Current gate: G4
```

## Próxima acción

Preparar la especificación Spec-Kit correspondiente a:

```text
Phase 4
Refresh tokens + sesiones + logout
```

asegurando que `spec.md`, `plan.md` y `tasks.md` no incorporen cambio o recuperación de contraseña, email ni otras capacidades pertenecientes a fases posteriores.
