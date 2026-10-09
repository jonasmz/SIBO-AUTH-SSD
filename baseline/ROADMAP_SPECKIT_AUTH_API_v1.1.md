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
| 4 | Refresh tokens + sesiones + logout | `[x]` | Phase 1-3 | `G4` |
| 5 | Cambio de contraseña | `[x]` | Phase 1-4 | `G5` |
| 6 | Recuperación de contraseña + email | `[x]` | Phase 1-5 | `G6` |
| 7 | Security hardening | `[x]` | Phase 1-6 | `G7` |
| 8 | Operación + integración + validación final | `[x]` | Phase 1-7 | `G8` |

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

- [x] Crear solución .NET.
- [x] Crear proyecto Authentication API.
- [x] Configurar ASP.NET Core.
- [x] Configurar Entity Framework Core.
- [x] Configurar SQLite.
- [x] Configurar ASP.NET Core Identity.
- [x] Configurar Dockerfile.
- [x] Configurar integración mínima con Docker Compose.
- [x] Configurar almacenamiento persistente SQLite independiente del ciclo de vida del proyecto Compose.
- [x] Utilizar como referencia un bind mount hacia un directorio explícito del host para SQLite.
- [x] Permitir volumen `external` como alternativa administrada fuera del proyecto Compose.
- [x] Documentar la ubicación persistente de SQLite.

### Inicialización de base

- [x] Authentication API prepara automáticamente la base durante startup.
- [x] No existe migration container.
- [x] Compose no ejecuta `dotnet ef database update`.
- [x] No existe script externo de bootstrap.
- [x] Aplicar migraciones embebidas pendientes desde la aplicación.
- [x] Startup es idempotente.
- [x] Fallo de migración impide readiness.

### Identity bootstrap

- [x] Crear rol `Administrator` cuando no exista.
- [x] Crear administrador inicial cuando no exista.
- [x] `UserName = admin`.
- [x] `Email = admin@local.invalid`.
- [x] Password inicial `admin`.
- [x] Asignar `Administrator`.
- [x] Reinicio no duplica usuario.
- [x] Reinicio no duplica rol.
- [x] Reinicio no sobrescribe cambios del usuario.

### Login

- [x] Implementar `POST /api/auth/login`.
- [x] Login por email.
- [x] Validación mediante Identity.
- [x] Usuario inexistente devuelve `401`.
- [x] Password inválida devuelve `401`.
- [x] Usuario deshabilitado queda preparado como estado de identidad si resulta necesario para Phase 3.
- [x] Activar contabilización de fallos para lockout.
- [x] Respuesta externa genérica.

### JWT

- [x] Configurar RS256.
- [x] Cargar clave privada desde configuración externa.
- [x] Montar la clave privada RSA desde almacenamiento independiente del ciclo de vida del proyecto Compose.
- [x] Evitar named volumes administrados por el propio Compose como única copia de la clave privada.
- [x] Configurar permisos restrictivos y montaje de sólo lectura cuando la plataforma lo permita.
- [x] Emitir access token.
- [x] Incluir `sub`.
- [x] Incluir `email`.
- [x] Incluir `role`.
- [x] Incluir `iss`.
- [x] Incluir `aud`.
- [x] Incluir `iat`.
- [x] Incluir `exp`.
- [x] Incluir `jti`.
- [x] Duración configurable.
- [x] Valor inicial recomendado: 15 minutos.

### Health

- [x] Implementar `/health/live`.
- [x] Implementar `/health/ready`.
- [x] Readiness comprueba disponibilidad de SQLite.
- [x] Readiness falla si startup de DB falla.

---

## 7.3 Fuera de alcance de Phase 1

No implementar:

- [x] Refresh tokens.
- [x] Logout real basado en sesiones.
- [x] CRUD administrativo.
- [x] Recuperación de contraseña.
- [x] Envío de correo.
- [x] Password reset.
- [x] Gestión completa de rate limiting.
- [x] Cookie de refresh.
- [x] Session family.
- [x] JWKS.
- [x] Rotación de claves.
- [x] Blacklist JWT.

---

## 7.4 Pruebas mínimas

### Startup

- [x] Base vacía inicia correctamente.
- [x] Schema se crea/aplica automáticamente.
- [x] Base existente inicia correctamente.
- [x] Reinicio es idempotente.

### Admin bootstrap

- [x] Se crea `Administrator`.
- [x] Se crea `admin`.
- [x] `admin@local.invalid` existe.
- [x] Password inicial `admin` autentica.
- [x] Segundo startup no duplica admin.

### Login

- [x] Login válido.
- [x] Password incorrecta.
- [x] Email inexistente.
- [x] Respuestas externas equivalentes.
- [x] Fallos contabilizan para Identity lockout.

### JWT

- [x] Token firmado con RS256.
- [x] Claims obligatorios.
- [x] Issuer correcto.
- [x] Audience correcto.
- [x] Expiración correcta.
- [x] Clave privada no aparece en responses/logs.

### Health

- [x] Liveness saludable.
- [x] Readiness saludable con DB disponible.
- [x] Readiness falla ante error de inicialización.

### Persistencia de despliegue

- [x] SQLite no reside únicamente en la capa efímera del contenedor.
- [x] SQLite no depende exclusivamente de un named volume administrado por el proyecto Compose.
- [x] La clave privada RSA no depende exclusivamente de un named volume administrado por el proyecto Compose.
- [x] Las rutas persistentes quedan documentadas.

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

- [x] Distribuir/configurar clave pública en API A.
- [x] Distribuir/configurar clave pública en API B.
- [x] Garantizar que ninguna reciba clave privada.

### JWT Bearer

En ambas APIs:

- [x] Configurar JWT Bearer.
- [x] Validar algoritmo esperado.
- [x] Validar firma.
- [x] Validar issuer.
- [x] Validar audience.
- [x] Validar expiración.
- [x] Mapear `sub`.
- [x] Mapear `role`.

### Autorización mínima

- [x] Proteger al menos un endpoint de prueba/real en API A.
- [x] Proteger al menos un endpoint de prueba/real en API B.
- [x] Diferenciar `401` de `403`.

---

## 8.3 Fuera de alcance de Phase 2

No implementar:

- [x] Refresh.
- [x] Usuarios administrativos.
- [x] Roles dinámicos.
- [x] Revocación.
- [x] Introspection.
- [x] JWKS.
- [x] Validación remota de tokens.
- [x] Blacklist.

---

## 8.4 Pruebas mínimas

- [x] JWT válido permite acceso en API A.
- [x] JWT válido permite acceso en API B.
- [x] Request sin JWT -> `401`.
- [x] Firma inválida -> `401`.
- [x] Token expirado -> `401`.
- [x] Issuer inválido -> `401`.
- [x] Audience inválido -> `401`.
- [x] Rol insuficiente -> `403`.
- [x] API A funciona sin consultar Auth API por request.
- [x] API B funciona sin consultar Auth API por request.

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

- [x] `GET /api/admin/users`.
- [x] `GET /api/admin/users/{id}`.
- [x] `POST /api/admin/users`.
- [x] `PATCH /api/admin/users/{id}`.
- [x] `POST /api/admin/users/{id}/enable`.
- [x] `POST /api/admin/users/{id}/disable`.

### Roles

- [x] `GET /api/admin/roles`.
- [x] `POST /api/admin/roles`.
- [x] `PATCH /api/admin/roles/{id}`.
- [x] `DELETE /api/admin/roles/{id}`.
- [x] `PUT /api/admin/users/{id}/roles`.

### Autorización

- [x] Proteger `/api/admin/*`.
- [x] Requerir `Administrator`.
- [x] `401` cuando no está autenticado.
- [x] `403` cuando está autenticado sin rol.

### Reglas de dominio

- [x] Email único normalizado.
- [x] Password cumple política Identity.
- [x] Roles únicos.
- [x] No eliminar rol con usuarios asignados.
- [x] No deshabilitar último administrador habilitado.
- [x] No retirar rol Administrator al último administrador habilitado.

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

- [x] RefreshToken.
- [x] SessionFamily.
- [x] Revoke sessions.
- [x] Logout stateful.
- [x] Email recovery.
- [x] Password reset.

No crear interfaces ficticias para estas capacidades.

---

## 9.4 Pruebas mínimas

### Users

- [x] Crear usuario.
- [x] Email duplicado rechazado.
- [x] Listar usuarios.
- [x] Consultar usuario.
- [x] Modificación válida.
- [x] Deshabilitar.
- [x] Usuario deshabilitado no puede login.
- [x] Rehabilitar.
- [x] Usuario rehabilitado puede volver a login.

### Roles

- [x] Crear rol.
- [x] Rol duplicado rechazado.
- [x] Asignar rol.
- [x] Retirar rol.
- [x] Eliminar rol no asignado.
- [x] Rechazar eliminación de rol asignado.

### Protección administrativa

- [x] Anónimo -> `401`.
- [x] Usuario normal -> `403`.
- [x] Administrator -> permitido.
- [x] Último administrador no puede deshabilitarse.
- [x] Último administrador no puede perder rol.

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

- [x] Definir modelo de refresh token.
- [x] Persistir hash, nunca token completo.
- [x] Definir familia de tokens.
- [x] Definir expiración absoluta.
- [x] Definir revocación.

---

## 10.3 Endpoints

- [x] `POST /api/auth/refresh`.
- [x] `POST /api/auth/logout`.
- [x] `POST /api/admin/users/{id}/revoke-sessions`.

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

- [x] Login crea refresh token.
- [x] Login crea FamilyId.
- [x] Refresh token se entrega al cliente.
- [x] Persistir solamente hash.

---

## 10.5 Refresh rotation

- [x] Validar token.
- [x] Validar expiración.
- [x] Validar estado del usuario.
- [x] Revocar token presentado.
- [x] Crear nuevo token.
- [x] Mantener FamilyId.
- [x] Emitir nuevo access token.

---

## 10.6 Replay detection

- [x] Detectar reutilización de token rotado.
- [x] Revocar familia completa.
- [x] Registrar evento de seguridad.

---

## 10.7 Logout

- [x] Revocar sesión/familia correspondiente.
- [x] Operación idempotente.
- [x] No introducir blacklist de JWT.
- [x] Access token vigente expira naturalmente.

---

## 10.8 Extensión de comportamiento anterior

A partir de esta fase:

```text
disable user
    |
    +--> impide login
    +--> revoca refresh sessions
```

- [x] Extender disable para revocar sesiones.
- [x] Enable no restaura sesiones revocadas.

---

## 10.9 Pruebas mínimas

- [x] Login crea sesión.
- [x] Refresh válido.
- [x] Refresh rota token.
- [x] Token anterior deja de funcionar.
- [x] Refresh expirado rechazado.
- [x] Token desconocido rechazado.
- [x] Reuse detectado.
- [x] Reuse revoca familia.
- [x] Usuario disabled no puede refresh.
- [x] Disable revoca sesiones.
- [x] Logout revoca sesión.
- [x] Logout idempotente.
- [x] Revoke-sessions administrativo.
- [x] Access token previo no requiere blacklist.

---

## 10.10 Criterio de salida — Gate G4

- [x] Login + refresh + logout forman un ciclo completo.
- [x] Rotación funciona.
- [x] Reuse detection funciona.
- [x] Disable revoca sesiones.
- [x] API A/B continúan validando JWT localmente.
- [x] Build PASS.
- [x] Tests PASS.
- [x] Regression Phase 1-3 PASS.
- [x] Commit de cierre creado.

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

- [x] Requiere autenticación.
- [x] Requiere password actual.
- [x] Requiere nueva password.
- [x] Validar password actual mediante Identity.
- [x] Aplicar política de password.
- [x] Actualizar password mediante Identity.
- [x] Revocar otras sesiones renovables.
- [x] No volver a establecer `admin` automáticamente.

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

- [x] SMTP.
- [x] `IEmailSender`.
- [x] Forgot password.
- [x] Reset token.

---

## 11.5 Pruebas mínimas

- [x] Endpoint exige JWT.
- [x] Password actual incorrecta rechazada.
- [x] Nueva password inválida rechazada.
- [x] Cambio exitoso.
- [x] Password anterior deja de autenticar.
- [x] Password nueva autentica.
- [x] Sesiones anteriores revocadas.
- [x] Restart no restaura `admin`.
- [x] Admin puede cambiar password sin email funcional externo.

---

## 11.6 Criterio de salida — Gate G5

- [x] Change password completo.
- [x] Admin inicial puede abandonar credencial por defecto.
- [x] Revocación de sesiones integrada.
- [x] Sin infraestructura de email anticipada.
- [x] Build PASS.
- [x] Tests PASS.
- [x] Regression Phase 1-4 PASS.
- [x] Commit de cierre creado.

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

- [x] Crear abstracción mínima de envío de correo.
- [x] Implementación SMTP.
- [x] Configuración externa.
- [x] No registrar secretos SMTP.
- [x] No registrar reset token.

---

## 12.3 Data Protection

- [x] Persistir Data Protection keys.
- [x] Utilizar almacenamiento independiente del ciclo de vida del proyecto Compose.
- [x] Utilizar bind mount de host como opción de referencia o volumen `external` como alternativa.
- [x] Verificar supervivencia a restart.
- [x] Verificar que la configuración no dependa de named volumes administrados por el propio Compose.
- [x] No agregar servicio externo.

---

## 12.4 Forgot password

Endpoint:

```text
POST /api/auth/forgot-password
```

- [x] Acceso anónimo.
- [x] Recibe email.
- [x] Respuesta genérica.
- [x] No revela existencia de usuario.
- [x] Generar token mediante Identity.
- [x] Solicitar envío de correo.

---

## 12.5 Reset password

Endpoint:

```text
POST /api/auth/reset-password
```

- [x] Recibe email.
- [x] Recibe reset token.
- [x] Recibe nueva password.
- [x] Validar token.
- [x] Validar password.
- [x] Cambiar password.
- [x] Revocar todas las sesiones renovables.

---

## 12.6 Pruebas mínimas

- [x] Forgot para usuario existente.
- [x] Forgot para usuario inexistente.
- [x] Misma respuesta externa.
- [x] Email sender invocado sólo cuando corresponde.
- [x] Reset token válido.
- [x] Reset token inválido.
- [x] Reset token alterado.
- [x] Reset token vencido cuando pueda probarse.
- [x] Reset cambia password.
- [x] Reset revoca sesiones.
- [x] Restart no invalida innecesariamente token aún válido.
- [x] El key ring de Data Protection está configurado fuera del ciclo de vida del proyecto Compose.
- [x] Reset tokens no aparecen en logs.

---

## 12.7 Criterio de salida — Gate G6

- [x] Forgot/reset completo.
- [x] SMTP desacoplado.
- [x] Data Protection persistente.
- [x] Anti-enumeración funcional.
- [x] Reset revoca sesiones.
- [x] Build PASS.
- [x] Tests PASS.
- [x] Regression Phase 1-5 PASS.
- [x] Commit de cierre creado.

---

# 13. Phase 7 — Security hardening

## 13.1 Objetivo

Aplicar protección transversal a la superficie de autenticación completa.

Se realiza en esta etapa porque ahora ya existen todos los endpoints sensibles.

---

## 13.2 Identity lockout

- [x] `MaxFailedAccessAttempts = 5` inicial.
- [x] `DefaultLockoutTimeSpan = 15 min` inicial.
- [x] Configurable externamente.
- [x] Login contabiliza fallos.
- [x] Respuesta no revela lockout.

---

## 13.3 Application rate limiting

Políticas independientes para:

- [x] `/login`.
- [x] `/refresh`.
- [x] `/forgot-password`.
- [x] `/reset-password`.

Requisitos:

- [x] Rate limiting por IP.
- [x] Configurable.
- [x] `429` al exceder límite.
- [x] Forgot puede limitar además por email normalizado.

---

## 13.4 Reverse proxy

- [x] Primera capa de rate limiting.
- [x] Forwarded headers.
- [x] Known proxies/networks.
- [x] No confiar en forwarded headers arbitrarios.
- [x] Verificar IP real de origen.

---

## 13.5 Refresh cookie

- [x] `HttpOnly`.
- [x] `Secure` en producción.
- [x] `SameSite` restrictivo.
- [x] Path limitado cuando corresponda.
- [x] Eliminar/inutilizar cookie en logout.
- [x] Access token preferentemente en memoria.

---

## 13.6 CSRF / origin controls

- [x] Refresh protegido frente a cross-site request indebido.
- [x] Logout protegido.
- [x] Validación de origen cuando corresponda.
- [x] CORS no se habilita si same-origin lo hace innecesario.
- [x] Si CORS es necesario, allowlist explícita.

---

## 13.7 Anti-enumeration

Comprobar equivalencia externa para:

```text
unknown account
wrong password
locked account
disabled account
```

- [x] Códigos equivalentes.
- [x] Mensajes equivalentes.
- [x] Timing razonablemente comparable.
- [x] Forgot no enumera.
- [x] Reset no revela información innecesaria.

---

## 13.8 Protección de secretos

- [x] Passwords fuera de logs.
- [x] Access tokens completos fuera de logs.
- [x] Refresh tokens fuera de logs.
- [x] Reset tokens fuera de logs.
- [x] RSA private key fuera de logs.
- [x] Secretos de email fuera de logs.

---

## 13.9 Pruebas mínimas

- [x] 5 fallos bloquean cuenta.
- [x] Lockout expira.
- [x] Rate limit login.
- [x] Rate limit refresh.
- [x] Rate limit forgot.
- [x] Rate limit reset.
- [x] Lockout por cuenta y rate limiting por IP son independientes.
- [x] `429` correcto.
- [x] Forwarded header spoofing no altera origen confiable.
- [x] Refresh cookie tiene flags requeridos.
- [x] Logout limpia cookie.
- [x] Request cross-origin indebido rechazado cuando corresponda.
- [x] Anti-enumeration responses.
- [x] Secret scan/log assertions.

---

## 13.10 Criterio de salida — Gate G7

- [x] Identity lockout completo.
- [x] Rate limiting completo.
- [x] Proxy trust configurado.
- [x] Cookie segura.
- [x] CSRF/origin cubierto.
- [x] Anti-enumeration cubierto.
- [x] Logs sin secretos.
- [x] Build PASS.
- [x] Tests PASS.
- [x] Regression Phase 1-6 PASS.
- [x] Commit de cierre creado.

---

# 14. Phase 8 — Operación, integración y validación final

## 14.1 Objetivo

Cerrar el producto como unidad desplegable y operable, validando la SRS completa sin incorporar nuevas capacidades funcionales.

Esta fase no deberá convertirse en una fase de features pendientes.

Si una feature funcional de fases anteriores falta, deberá corregirse en su fase conceptual antes de declarar cierre.

---

## 14.2 Logging y observabilidad

- [x] Logging estructurado.
- [x] Correlation ID.
- [x] Login success.
- [x] Login failure.
- [x] Lockout.
- [x] Rate limit.
- [x] Logout.
- [x] Password change.
- [x] Password reset.
- [x] User create.
- [x] User disable/enable.
- [x] Role assign/remove.
- [x] Refresh reuse detected.
- [x] Sessions revoked.

---

## 14.3 OpenAPI

- [x] Contrato OpenAPI generado.
- [x] Requests documentados.
- [x] Responses documentadas.
- [x] Auth Bearer documentada.
- [x] Documentation UI limitada según ambiente.

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

- [x] No migration container.
- [x] No bootstrap container.
- [x] No Redis.
- [x] No Vault.
- [x] No servicio Identity externo.
- [x] Frontend/reverse proxy funciona.
- [x] El navegador enruta `/auth/*`, `/api-a/*` y `/api-b/*` exclusivamente a través del punto de entrada del frontend/reverse proxy.
- [x] Auth API sin puerto público directo en producción.
- [x] API A y API B sin exposición directa necesaria para el navegador.
- [x] Internal network correcta.

---

## 14.5 Persistencia y resiliencia del almacenamiento

Validar persistencia de:

- [x] SQLite.
- [x] Identity users.
- [x] Roles.
- [x] Refresh sessions.
- [x] Data Protection keys.
- [x] RSA signing key.

Validar además:

- [x] SQLite reside en almacenamiento independiente del ciclo de vida del proyecto Compose.
- [x] Data Protection reside en almacenamiento independiente del ciclo de vida del proyecto Compose.
- [x] RSA private key reside en almacenamiento independiente del ciclo de vida del proyecto Compose.
- [x] Las rutas persistentes están documentadas.
- [x] La eliminación de datos requiere una acción explícita sobre el almacenamiento persistente.

---

## 14.6 Restart / rebuild / teardown validation

Validar primero el ciclo ordinario de restart/rebuild, comprobando que:

- [x] DB permanece.
- [x] Usuarios permanecen.
- [x] Roles permanecen.
- [x] Password modificada del admin permanece.
- [x] Refresh/session state esperado permanece.
- [x] Data Protection permanece.
- [x] Signing key permanece.
- [x] API A sigue validando.
- [x] API B sigue validando.

En un entorno descartable equivalente a producción, validar explícitamente el escenario destructivo para volúmenes administrados por Compose:

- [x] Ejecutar `docker compose down -v`.
- [x] Verificar que SQLite continúa existiendo en el almacenamiento persistente externo al Compose.
- [x] Verificar que el key ring de Data Protection continúa existiendo.
- [x] Verificar que la clave privada RSA continúa existiendo.
- [x] Ejecutar nuevamente `docker compose up -d`.
- [x] Verificar que usuarios y roles continúan disponibles.
- [x] Verificar que la password modificada del administrador continúa vigente.
- [x] Verificar que API A y API B pueden seguir validando tokens emitidos con la clave persistida.

---

## 14.7 Backup y restauración de SQLite

- [x] Documentar procedimiento de backup consistente de SQLite.
- [x] El procedimiento no requiere un servicio permanente adicional.
- [x] Evitar copias inconsistentes durante escrituras concurrentes.
- [x] Documentar procedimiento de restauración.
- [x] Crear un backup de prueba.
- [x] Restaurarlo en un entorno descartable.
- [x] Iniciar Authentication API contra la base restaurada.
- [x] Verificar usuarios.
- [x] Verificar roles.
- [x] Verificar autenticación de al menos una cuenta conocida.

---

## 14.8 End-to-end acceptance flow

- [x] Deploy desde almacenamiento vacío.
- [x] Admin automático.
- [x] Login admin.
- [x] Cambiar password admin.
- [x] Crear usuario.
- [x] Crear/asignar rol.
- [x] Login usuario.
- [x] Acceso API A.
- [x] Acceso API B.
- [x] Refresh.
- [x] Rotation.
- [x] Logout.
- [x] Re-login.
- [x] Forgot password.
- [x] Reset password.
- [x] Sesiones anteriores revocadas.
- [x] Disable user.
- [x] Login rechazado.
- [x] Rate limit demostrado.
- [x] Lockout demostrado.
- [x] `docker compose down -v` no destruye SQLite, Data Protection ni clave RSA.
- [x] Backup/restore SQLite demostrado.
- [x] Acceso externo únicamente a través del reverse proxy.

---

## 14.9 Criterio de salida — Gate G8

La implementación completa podrá considerarse terminada cuando:

- [x] Todos los requisitos MVP de la SRS estén implementados.
- [x] Todos los gates G1-G7 permanezcan PASS.
- [x] Compose final sea autocontenido.
- [x] No existan pasos manuales de migración.
- [x] No exista bootstrap externo.
- [x] Persistencia crítica sea independiente del ciclo de vida de Compose.
- [x] `docker compose down -v` survival PASS en entorno de aceptación.
- [x] SQLite backup PASS.
- [x] SQLite restore PASS.
- [x] Reverse proxy routing PASS.
- [x] E2E acceptance PASS.
- [x] Tests completos PASS.
- [x] Build PASS.
- [x] Logs revisados.
- [x] OpenAPI revisado.
- [x] Documentación de operación actualizada.
- [x] Commit/tag de cierre creado.

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
| 2026-10-08 | Phase 4 | Complete — G4 approved | Sesiones renovables en Authentication API: cookie `auth_refresh` (HttpOnly, SameSite=Strict, Secure en producción) emitida en login, `POST /api/auth/refresh` con rotación en la misma familia y expiración absoluta fija, detección de replay con revocación de la familia, consumo concurrente de una sola credencial, `POST /api/auth/logout` idempotente, `POST /api/admin/users/{id}/revoke-sessions`, revocación de todas las familias al deshabilitar, validación exacta de Origin y estado en SQLite con migración al arrancar; sin blacklist de JWT | Build 0 warnings; 104/104 tests; `tests/acceptance/phase-4.sh` ALL PASS (incluye regresión `phase-3.sh`, `phase-2.sh` y `phase-1.sh`) | Commit de cierre `[Phase 4] Close Gate G4` | Aprobación explícita de G4 por el responsable del proyecto el 2026-10-08; evidencia en `docs/phase-4-operations.md`; revisión de convergencia sin hallazgos pendientes; nueva configuración requerida `Security:FrontendOrigin` (`AUTH_FRONTEND_ORIGIN`) y opcional `RefreshSession:LifetimeDays` (7 por defecto); rate limiting de refresh diferido a Phase 7 |
| 2026-10-08 | Phase 5 | Complete — G5 approved | `POST /api/auth/change-password` para cualquier usuario autenticado sobre su propia cuenta (cuenta tomada sólo del `sub`): verificación de la password actual, política y actualización mediante Identity (`ChangePasswordAsync`), revocación atómica (misma transacción) de las demás familias renovables con motivo `PasswordChanged` conservando la familia de la cookie `auth_refresh` utilizable del mismo usuario (sin cookie utilizable se revocan todas), el administrador inicial `admin/admin` puede retirar su credencial sin email y el nuevo secreto sobrevive al restart; sin blacklist de JWT, sin migración ni infraestructura de email | Build 0 warnings; 115/115 tests; `tests/acceptance/phase-5.sh` ALL PASS (incluye regresión `phase-4.sh`, `phase-3.sh`, `phase-2.sh` y `phase-1.sh`) | Commit de cierre `[Phase 5] Close Gate G5` | Aprobación explícita de G5 por el responsable del proyecto el 2026-10-08; evidencia en `docs/phase-5-operations.md`; revisión de convergencia sin hallazgos; sin configuración nueva; un fallo de password actual no cuenta para el lockout y el rate limiting de este endpoint queda para Phase 7 |
| 2026-10-08 | Phase 6 | Complete — G6 approved | `POST /api/auth/forgot-password` anónimo con respuesta genérica idéntica (`204`) para cuentas existentes, inexistentes y deshabilitadas, incluso ante fallo de envío o de emisión del token; token de restablecimiento emitido y validado por Identity (`DataProtectorTokenProvider`, temporal, de un solo uso por rotación del security stamp, sin almacén de tokens) y entregado por correo mediante el puerto `IEmailSender` con adaptador SMTP MailKit sólo en Infrastructure; `POST /api/auth/reset-password` anónimo con un único `401` para token inválido/alterado/vencido/usado, email desconocido o cuenta deshabilitada, y revocación atómica (misma transacción) de todas las familias renovables con motivo `PasswordReset`; key ring de Data Protection persistido en bind mount del host fuera del ciclo de vida de Compose (sobrevive a restart, recreación y `down -v`); sin blacklist de JWT ni migración | Build 0 warnings; 157/157 tests; `tests/acceptance/phase-6.sh` ALL PASS (incluye regresión `phase-5.sh`, `phase-4.sh`, `phase-3.sh`, `phase-2.sh` y `phase-1.sh`) | Commit de cierre `[Phase 6] Close Gate G6` | Aprobación explícita de G6 por el responsable del proyecto el 2026-10-08; evidencia en `docs/phase-6-operations.md`; revisión de convergencia sin hallazgos; nueva configuración requerida `Smtp:*` (`AUTH_SMTP_*`) y `DataProtection:KeysPath` (`AUTH_DATAPROTECTION_HOST_PATH`); único paquete nuevo MailKit 4.18.0; el sumidero SMTP Mailpit existe sólo en el override de aceptación; rate limiting de forgot/reset (SRS NFR-SEC-BF-006..009) queda para Phase 7 |
| 2026-10-08 | Phase 7 | Complete — G7 approved | Endurecimiento transversal sin nuevos endpoints ni cambios de contrato: lockout de Identity con valores iniciales explícitos (5 fallos, 15 minutos) configurables externamente; rate limiting de aplicación con políticas independientes de ventana fija por IP efectiva para login, refresh, forgot-password y reset-password (middleware del framework, contadores sólo en memoria) y límite adicional por email normalizado en forgot-password, todos con `429` ProblemDetails y `Retry-After`; forwarded headers (`X-Forwarded-For`/`X-Forwarded-Proto`) honrados sólo desde proxies o redes configurados explícitamente y desactivados por completo sin configuración; eventos estructurados `LoginFailed` (causa real), `AccountLockedOut` y `RateLimitApplied` sin secretos; cookie, origen/CSRF, ausencia de CORS y anti-enumeración verificados sin cambios; configuración de referencia de Nginx (primera capa de limitación y cabeceras sobrescritas) verificada en un proxy desechable sólo de aceptación | Build 0 warnings; 181/181 tests; `tests/acceptance/phase-7.sh` ALL PASS (incluye regresión `phase-6.sh`, `phase-5.sh`, `phase-4.sh`, `phase-3.sh`, `phase-2.sh` y `phase-1.sh`) | Commit de cierre `[Phase 7] Close Gate G7` | Aprobación explícita de G7 por el responsable del proyecto el 2026-10-08; evidencia en `docs/phase-7-operations.md`; revisión de convergencia sin hallazgos; sin paquetes nuevos ni migración; nueva configuración opcional `Identity:Lockout:*` (`AUTH_LOCKOUT_*`), `RateLimiting:*` (`AUTH_RATE_LIMIT_*`) y `ReverseProxy:*` (`AUTH_TRUSTED_PROXIES`/`AUTH_TRUSTED_NETWORKS`); valores por defecto de los límites como decisión de proyecto documentada; el proxy de referencia existe sólo en el override de aceptación y `compose.yml` mantiene `auth-api`, `api-a` y `api-b`; despliegue productivo del proxy, TLS y logging persistente quedan para Phase 8 |
| 2026-10-08 | Phase 8 | Complete — G8 approved | Cierre operativo e integración sin nuevas capacidades funcionales: proveedor de logging a archivo propio (`PersistentFileLoggerProvider`, cola en memoria con un único escritor en segundo plano, archivos diarios UTC `auth-yyyy-MM-dd.log`, retención configurable de 30 días, resistente a fallos de I/O) con los mismos eventos que la consola y los seis eventos faltantes (`LoginSucceeded`, `UserCreated`, `UserEnabled`, `UserDisabled`, `UserRoleAssigned`, `UserRoleRemoved`); contrato OpenAPI 3.1 de las 20 operaciones con esquema `Bearer` y visor Scalar de sólo lectura, ambos sólo en Development; `compose.yml` productivo con exactamente `frontend` (Nginx oficial, TLS, archivos estáticos aportados por el responsable y reverse proxy), `auth-api`, `api-a` y `api-b`, sin puertos de backend publicados, URLs públicas `/auth/*` y `/auth/admin/*` traducidas a las rutas internas sin cambiarlas, cookie `auth_refresh` reescrita a `Path=/auth`, confianza en forwarded headers sólo desde la dirección fija del frontend y clave privada montada sólo en `auth-api`; persistencia crítica y logs en bind mounts del host que sobreviven a `down -v`; backup SQLite en línea (`.backup` + `PRAGMA integrity_check`) restaurado y verificado en un proyecto desechable | Build 0 warnings; 203/203 tests; `tests/acceptance/phase-8.sh` ALL PASS (despliegue, ciclo de vida y `down -v`, backup/restore, E2E por las URLs públicas, revisión de logs y regresión `phase-7.sh` a `phase-1.sh`) | Commit de cierre `[Phase 8] Close Gate G8` y tag `gate-g8` | Aprobación explícita de G8 por el responsable del proyecto el 2026-10-08; evidencia en `docs/phase-8-operations.md`; revisión de convergencia sin hallazgos (tras T038); paquetes nuevos sólo `Microsoft.AspNetCore.OpenApi` 10.0.12, `Scalar.AspNetCore` 2.17.9 y el transitivo fijado `Microsoft.OpenApi`; sin migración; nueva configuración requerida `Logging:File:Directory` (`AUTH_LOGS_HOST_PATH`), `FRONTEND_STATIC_HOST_PATH` y `FRONTEND_TLS_HOST_PATH`, y opcional `AUTH_LOG_RETENTION_DAYS`, `AUTH_INTERNAL_SUBNET` y `FRONTEND_INTERNAL_ADDRESS`; la aplicación Angular compilada es un insumo del proyecto frontend |

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
Phase 4: COMPLETE (Gate G4 approved 2026-10-08)
Phase 5: COMPLETE (Gate G5 approved 2026-10-08)
Phase 6: COMPLETE (Gate G6 approved 2026-10-08)
Phase 7: COMPLETE (Gate G7 approved 2026-10-08)
Phase 8: COMPLETE (Gate G8 approved 2026-10-08)
Current phase: — (MVP complete)
Current gate: — (G1-G8 closed)
```

## Próxima acción

Ninguna fase pendiente: las ocho fases del roadmap están completas y los gates G1-G8 cerrados (tag `gate-g8`).

Cualquier trabajo posterior (nuevas capacidades, cambios de contrato o de la baseline) requiere una enmienda explícita de la SRS o de las Technical Constraints antes de especificarse; un defecto de una capacidad existente se corrige en su fase conceptual con su requisito de origen.
