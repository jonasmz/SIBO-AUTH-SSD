# Software Requirements Specification (SRS)
## Servicio Central de Autenticación y Autorización

**Versión:** 1.1  
**Estado:** Baseline revisada  
**Fecha:** 2026-10-06  
**Referencia de calidad:** ISO/IEC/IEEE 29148:2018  
**Producto:** Authentication API  
**Tipo:** Web API REST interna  
**Stack obligatorio:** ASP.NET Core / ASP.NET Core Identity / Entity Framework Core / SQLite / Docker Compose

## Control de cambios

| Versión | Fecha | Cambio |
|---|---|---|
| 1.0 | 2026-10-06 | Baseline inicial. |
| 1.1 | 2026-10-06 | Refuerzo de persistencia fuera del ciclo de vida de Compose, supervivencia ante `docker compose down -v`, procedimiento de backup/restore y revisión de calidad de requisitos alineada con ISO/IEC/IEEE 29148:2018. |

---

## 1. Propósito del documento

Este documento especifica de forma verificable, trazable y no ambigua los requisitos de un servicio central de autenticación y autorización destinado a un sistema compuesto por:

- un frontend SPA desarrollado en Angular;
- una API de negocio A;
- una API de negocio B;
- la Authentication API definida en esta especificación.

La Authentication API será la autoridad central para la identidad de los usuarios y para la emisión de credenciales de acceso consumidas por las dos APIs de negocio.

La especificación sigue los principios de calidad de requisitos de ISO/IEC/IEEE 29148:2018: los requisitos deberán ser necesarios, inequívocos, completos dentro del alcance definido, consistentes, realizables, verificables y trazables.

---

## 2. Objetivo del producto

El producto deberá proporcionar un servicio sencillo de identidad que permita:

1. iniciar sesión;
2. cerrar sesión;
3. renovar sesiones mediante refresh tokens;
4. recuperar y restablecer contraseñas;
5. administrar usuarios;
6. habilitar y deshabilitar usuarios;
7. administrar roles;
8. asignar y retirar roles;
9. emitir access tokens válidos para las APIs de negocio;
10. revocar sesiones;
11. proteger los mecanismos de autenticación frente a abuso y fuerza bruta;
12. desplegarse de forma directa mediante Docker Compose junto con el frontend y las dos APIs de negocio.

La solución deberá evitar deliberadamente incorporar características propias de una plataforma IAM completa cuando no sean necesarias para este sistema.

---

## 3. Alcance

### 3.1 Funcionalidad incluida

El sistema incluirá:

- autenticación mediante correo electrónico y contraseña para usuarios normales;
- usuario administrador inicial creado automáticamente;
- ASP.NET Core Identity como mecanismo de gestión de identidad;
- administración de usuarios;
- administración de roles;
- control de acceso basado en roles;
- JWT de corta duración;
- firma JWT asimétrica RS256;
- refresh tokens opacos;
- rotación de refresh tokens;
- detección de reutilización de refresh tokens;
- logout;
- revocación de sesiones;
- recuperación y restablecimiento de contraseña;
- bloqueo por intentos fallidos mediante ASP.NET Core Identity;
- rate limiting en la capa de aplicación y/o infraestructura;
- persistencia SQLite;
- inicialización automática del esquema de base de datos;
- inicialización automática del administrador;
- health checks;
- OpenAPI para desarrollo y operación interna;
- integración mediante Docker Compose;
- validación local de JWT por parte de las APIs de negocio.

### 3.2 Funcionalidad fuera de alcance

Quedan expresamente fuera del alcance inicial:

- registro público de usuarios;
- login social;
- Google, Microsoft, Apple, Meta u otros proveedores externos;
- OAuth 2.0 Authorization Server completo;
- OpenID Connect Provider;
- SAML;
- LDAP;
- Active Directory;
- MFA;
- TOTP;
- passkeys/WebAuthn;
- multitenancy;
- organizaciones;
- grupos jerárquicos;
- permisos granulares distintos de roles;
- políticas ABAC;
- administración avanzada de dispositivos;
- lista distribuida de revocación de access tokens;
- Redis;
- caché distribuida;
- introspection endpoint;
- rotación automática programada de claves JWT;
- Vault, KMS o HSM como requisito;
- interfaz web de administración propia de Authentication API;
- eliminación física rutinaria de usuarios;
- almacenamiento de información funcional perteneciente a las APIs de negocio.

---

## 4. Contexto del sistema

### 4.1 Topología lógica

La topología objetivo será:

```text
                    Cliente / Navegador
                           |
                           | HTTPS
                           v
              +---------------------------+
              | Frontend / Reverse Proxy  |
              | Angular + servidor web    |
              +-------------+-------------+
                            |
                    red interna Docker
                            |
             +--------------+--------------+
             |              |              |
             v              v              v
        Auth API       Business API A  Business API B
             |
             v
           SQLite
```

El contenedor encargado de servir la aplicación Angular podrá actuar también como reverse proxy para mantener el despliegue total en cuatro servicios.

No será obligatorio desplegar un quinto servicio dedicado exclusivamente a gateway o reverse proxy.

### 4.2 Enrutamiento conceptual

La exposición externa podrá utilizar un único origen, por ejemplo:

```text
/                   -> frontend Angular
/auth/*             -> Authentication API
/api-a/*            -> Business API A
/api-b/*            -> Business API B
```

Los paths exactos serán configurables y no forman parte del contrato funcional mientras se mantenga la separación lógica indicada.

### 4.3 Red interna

Authentication API, Business API A y Business API B deberán poder comunicarse mediante una red privada de Docker Compose.

Authentication API no deberá publicar directamente su puerto de aplicación hacia el exterior en producción.

Toda solicitud originada desde el navegador deberá ingresar mediante el punto de entrada autorizado del sistema.

---

## 5. Actores

### 5.1 Usuario anónimo

Actor que todavía no dispone de un access token válido.

Podrá:

- iniciar sesión;
- solicitar recuperación de contraseña;
- completar un restablecimiento de contraseña;
- utilizar un refresh token válido para renovar una sesión.

### 5.2 Usuario autenticado

Usuario que dispone de un access token válido.

Podrá:

- acceder a las APIs de negocio según sus roles;
- cerrar sesión;
- cambiar su propia contraseña cuando corresponda.

### 5.3 Administrador

Usuario autenticado con el rol administrativo.

Podrá:

- crear usuarios;
- consultar usuarios;
- modificar atributos administrativos permitidos;
- habilitar y deshabilitar usuarios;
- asignar roles;
- retirar roles;
- crear roles;
- consultar roles;
- modificar roles;
- eliminar roles cuando las reglas lo permitan;
- revocar sesiones de usuarios.

### 5.4 API consumidora

Servicio backend que:

- recibe JWT emitidos por Authentication API;
- valida los JWT localmente;
- utiliza `sub` para identificar al usuario;
- utiliza claims de rol para autorización.

### 5.5 Operador de despliegue

Responsable de:

- suministrar configuración;
- suministrar claves de firma;
- montar almacenamiento persistente;
- iniciar los servicios mediante Docker Compose;
- realizar backups operativos del archivo SQLite cuando corresponda.

---

## 6. Definiciones y abreviaturas

| Término | Definición |
|---|---|
| Access token | JWT de corta duración usado para acceder a recursos protegidos. |
| Refresh token | Credencial opaca de mayor duración utilizada para obtener nuevos access tokens. |
| Identity | ASP.NET Core Identity. |
| JWT | JSON Web Token. |
| RS256 | Firma RSA usando SHA-256. |
| Claim | Afirmación incluida en un token. |
| Role / Rol | Unidad simple de autorización asignable a usuarios. |
| Sesión | Relación lógica iniciada por un login exitoso y asociada a una familia de refresh tokens. |
| Familia de refresh tokens | Cadena de refresh tokens producidos mediante rotación a partir de una sesión. |
| Lockout | Bloqueo temporal de una cuenta por intentos fallidos. |
| Reverse proxy | Componente de entrada que enruta solicitudes externas hacia servicios internos. |
| Idempotente | Operación que puede repetirse sin producir resultados acumulativos no deseados. |
| UTC | Coordinated Universal Time. |

---

# 7. Supuestos y restricciones

## 7.1 Restricciones tecnológicas

**CR-TECH-001.** Authentication API deberá implementarse como una Web API basada en ASP.NET Core.

**CR-TECH-002.** La administración de identidades deberá utilizar ASP.NET Core Identity.

**CR-TECH-003.** La persistencia deberá utilizar Entity Framework Core.

**CR-TECH-004.** La base de datos deberá ser SQLite.

**CR-TECH-005.** El sistema deberá ejecutarse en un contenedor Linux.

**CR-TECH-006.** El despliegue deberá ser compatible con Docker Compose.

**CR-TECH-007.** Los access tokens deberán utilizar JWT.

**CR-TECH-008.** Los JWT deberán firmarse utilizando RS256.

## 7.2 Restricciones de complejidad

**CR-SIM-001.** La solución no deberá requerir Redis para cumplir esta SRS.

**CR-SIM-002.** La solución no deberá requerir un servidor OAuth/OpenID Connect externo.

**CR-SIM-003.** La solución no deberá requerir Vault, KMS o HSM.

**CR-SIM-004.** La solución no deberá requerir un servicio adicional de migraciones o bootstrap.

**CR-SIM-005.** La solución no deberá requerir que las APIs de negocio accedan a la base de datos de Authentication API.

**CR-SIM-006.** La solución no deberá requerir una llamada a Authentication API por cada request recibido por las APIs de negocio.

---

# 8. Modelo de identidad

## 8.1 Usuario normal

**FR-ID-001.** Los usuarios normales deberán autenticarse utilizando correo electrónico y contraseña.

**FR-ID-002.** El correo electrónico deberá ser único dentro del sistema tras su normalización.

**FR-ID-003.** El login de usuarios normales no deberá requerir ni exponer un username como identificador de autenticación.

**FR-ID-004.** El sistema deberá normalizar el correo electrónico mediante los mecanismos de Identity antes de evaluar unicidad y autenticación.

**FR-ID-005.** Las contraseñas deberán almacenarse exclusivamente mediante el mecanismo de hashing de ASP.NET Core Identity.

## 8.2 Administrador inicial

Para satisfacer el requisito de disponer de un administrador conocido inmediatamente después del primer despliegue, se define una cuenta integrada.

**FR-ADMIN-BOOT-001.** En la primera inicialización de una base de datos nueva, la aplicación deberá crear automáticamente un usuario administrador.

**FR-ADMIN-BOOT-002.** El valor de `UserName` interno de esta cuenta deberá ser:

```text
admin
```

**FR-ADMIN-BOOT-003.** La contraseña inicial deberá ser:

```text
admin
```

**FR-ADMIN-BOOT-004.** Para mantener el contrato general de autenticación por correo electrónico, la cuenta administrativa integrada deberá poseer como correo inicial:

```text
admin@local.invalid
```

Por lo tanto, el login HTTP continuará utilizando el campo `email`.

Las credenciales iniciales efectivas serán:

```text
email:    admin@local.invalid
password: admin
```

y el valor interno de usuario será `admin`.

**FR-ADMIN-BOOT-005.** El administrador integrado deberá recibir automáticamente el rol administrativo principal.

**FR-ADMIN-BOOT-006.** La creación del administrador deberá ser idempotente.

**FR-ADMIN-BOOT-007.** Un reinicio posterior de la aplicación no deberá recrear la cuenta ni sobrescribir su contraseña, correo, roles o cambios realizados después de la creación inicial.

**FR-ADMIN-BOOT-008.** La lógica de creación del administrador deberá ejecutarse dentro de Authentication API y no como script, contenedor, job o comando externo de Docker Compose.

### 8.2.1 Consideración de seguridad del administrador inicial

La combinación inicial `admin/admin` constituye deliberadamente una credencial débil para simplificar el primer acceso.

Por este motivo:

**NFR-SEC-ADMIN-001.** La documentación operativa deberá identificar explícitamente que la contraseña inicial `admin` debe ser sustituida después del primer acceso.

**NFR-SEC-ADMIN-002.** Authentication API deberá proporcionar una operación autenticada que permita al usuario cambiar su propia contraseña.

**NFR-SEC-ADMIN-003.** La contraseña del administrador no deberá ser restablecida automáticamente a `admin` tras un restart, rebuild o redeploy.

---

# 9. Roles

## 9.1 Rol administrativo

**FR-ROLE-001.** El sistema deberá crear automáticamente un rol administrativo principal durante la primera inicialización.

El nombre canónico deberá ser:

```text
Administrator
```

**FR-ROLE-002.** La inicialización del rol deberá ser idempotente.

**FR-ROLE-003.** El administrador inicial deberá quedar asignado al rol `Administrator`.

## 9.2 Administración de roles

**FR-ROLE-004.** Un administrador deberá poder listar los roles existentes.

**FR-ROLE-005.** Un administrador deberá poder crear roles.

**FR-ROLE-006.** Los nombres normalizados de roles deberán ser únicos.

**FR-ROLE-007.** Un administrador deberá poder renombrar roles, excepto cuando la operación viole una regla de integridad definida en esta SRS.

**FR-ROLE-008.** Un administrador deberá poder eliminar un rol solamente cuando éste no esté asignado a ningún usuario.

**FR-ROLE-009.** El rol administrativo principal no deberá poder eliminarse mientras sea requerido para la administración del sistema.

## 9.3 Asignación de roles

**FR-ROLE-010.** Un administrador deberá poder asignar uno o más roles existentes a un usuario.

**FR-ROLE-011.** Un administrador deberá poder retirar roles de un usuario.

**FR-ROLE-012.** Los nuevos access tokens deberán reflejar los roles vigentes al momento de su emisión.

**FR-ROLE-013.** La modificación de roles no deberá modificar retroactivamente JWT ya emitidos.

**FR-ROLE-014.** El sistema no deberá permitir retirar el rol administrativo al último administrador habilitado.

**FR-ROLE-015.** El sistema no deberá permitir deshabilitar al último administrador habilitado.

---

# 10. Login

## 10.1 Endpoint

El sistema deberá proporcionar conceptualmente:

```http
POST /api/auth/login
```

## 10.2 Solicitud

**FR-LOGIN-001.** El request deberá contener:

```json
{
  "email": "usuario@example.com",
  "password": "********"
}
```

**FR-LOGIN-002.** El endpoint deberá permitir acceso anónimo en términos de autorización JWT.

**FR-LOGIN-003.** Que el endpoint sea anónimo no deberá implicar ausencia de protecciones de seguridad.

## 10.3 Validación

**FR-LOGIN-004.** Las credenciales deberán validarse mediante ASP.NET Core Identity.

**FR-LOGIN-005.** Los intentos fallidos deberán contabilizarse para el mecanismo de lockout de Identity.

**FR-LOGIN-006.** La implementación equivalente a `PasswordSignInAsync` deberá activar el comportamiento de lockout ante fallos.

**FR-LOGIN-007.** Un usuario deshabilitado no deberá poder iniciar sesión.

**FR-LOGIN-008.** Un usuario temporalmente bloqueado no deberá poder iniciar sesión hasta vencer el lockout.

## 10.4 Respuesta exitosa

**FR-LOGIN-009.** Ante autenticación exitosa el sistema deberá emitir:

- un access token;
- una sesión basada en refresh token;
- información de expiración del access token.

**FR-LOGIN-010.** El access token deberá devolverse en el body de la respuesta de login y refresh.

**FR-LOGIN-011.** El refresh token destinado al navegador deberá almacenarse mediante cookie `HttpOnly`.

## 10.5 Respuesta de fallo

**FR-LOGIN-012.** Los siguientes casos deberán producir una respuesta externamente equivalente:

- correo inexistente;
- contraseña incorrecta;
- cuenta bloqueada;
- cuenta deshabilitada.

**FR-LOGIN-013.** La respuesta no deberá revelar cuál de las condiciones anteriores ocurrió.

**FR-LOGIN-014.** El código de respuesta para credenciales no válidas deberá ser `401 Unauthorized`, salvo que una política general de rate limiting produzca `429 Too Many Requests`.

**FR-LOGIN-015.** El sistema deberá minimizar diferencias observables de tiempo que permitan enumerar cuentas de forma práctica.

---

# 11. Access tokens

## 11.1 Formato

**FR-JWT-001.** Los access tokens deberán utilizar formato JWT.

**FR-JWT-002.** Los access tokens deberán firmarse mediante RS256.

**FR-JWT-003.** La clave privada de firma deberá estar disponible exclusivamente para Authentication API.

**FR-JWT-004.** Las APIs de negocio deberán disponer solamente del material público necesario para verificar la firma.

## 11.2 Claims

**FR-JWT-005.** Cada access token deberá incluir como mínimo:

- `sub`;
- `email`;
- uno o más claims `role` cuando correspondan;
- `iss`;
- `aud`;
- `iat`;
- `exp`;
- `jti`.

**FR-JWT-006.** `sub` deberá contener un identificador estable del usuario.

**FR-JWT-007.** El identificador estable no deberá depender del correo electrónico, para permitir cambios de correo sin modificar la identidad lógica.

## 11.3 Expiración

**FR-JWT-008.** La duración del access token deberá ser configurable.

**FR-JWT-009.** El valor por defecto del access token deberá ser de 15 minutos, y deberá poder modificarse mediante configuración externa.

**FR-JWT-010.** Todas las APIs consumidoras deberán validar expiración.

## 11.4 Validación por APIs de negocio

**FR-JWT-011.** Business API A y Business API B deberán validar localmente:

- firma;
- algoritmo;
- issuer;
- audience;
- expiración.

**FR-JWT-012.** Las APIs consumidoras no deberán delegar la validación ordinaria de cada request a Authentication API.

---

# 12. Gestión de claves JWT

## 12.1 Modelo simple

La gestión de claves se mantendrá deliberadamente sencilla.

**FR-KEY-001.** El sistema deberá utilizar un único par activo de claves RSA para firma de JWT.

**FR-KEY-002.** La clave privada deberá ser persistente entre reinicios del contenedor.

**FR-KEY-003.** La clave privada no deberá almacenarse dentro del repositorio de código fuente.

**FR-KEY-004.** La clave privada no deberá incorporarse a la imagen Docker.

**FR-KEY-005.** La clave pública deberá suministrarse a las dos APIs de negocio mediante configuración externa al código fuente.

**FR-KEY-006.** El frontend Angular no deberá recibir la clave privada.

## 12.2 Rotación

**FR-KEY-007.** La rotación automática de claves queda fuera del alcance inicial.

**FR-KEY-008.** El sistema deberá permitir reemplazar manualmente el par de claves mediante configuración y redeploy, sin modificar código fuente.

**FR-KEY-009.** JWKS no será obligatorio para el MVP.

**FR-KEY-010.** La ausencia de JWKS no deberá impedir que las APIs de negocio validen JWT utilizando la clave pública configurada.

**FR-KEY-011.** En producción, la clave privada RSA deberá almacenarse en un archivo o almacenamiento cuyo ciclo de vida sea independiente de los contenedores y de los volúmenes administrados por el proyecto Docker Compose.

**FR-KEY-012.** La ejecución de `docker compose down -v` no deberá eliminar la clave privada RSA utilizada por Authentication API.

**FR-KEY-013.** El archivo de clave privada deberá montarse en Authentication API con permisos restrictivos y, cuando la plataforma lo permita, en modo de sólo lectura.

---

# 13. Refresh tokens

## 13.1 Características

**FR-REFRESH-001.** Los refresh tokens deberán ser valores opacos y criptográficamente aleatorios.

**FR-REFRESH-002.** Los refresh tokens no deberán ser JWT utilizados como credenciales de acceso a APIs de negocio.

**FR-REFRESH-003.** El valor completo del refresh token no deberá almacenarse en texto plano en la base de datos.

**FR-REFRESH-004.** El servidor deberá almacenar una representación criptográfica que permita verificar el refresh token recibido.

## 13.2 Rotación

**FR-REFRESH-005.** Todo uso exitoso de un refresh token deberá invalidar el token presentado y emitir uno nuevo.

**FR-REFRESH-006.** Los refresh tokens consecutivos de una misma sesión deberán asociarse a una familia común.

**FR-REFRESH-007.** Un refresh token ya utilizado no deberá ser aceptado nuevamente.

**FR-REFRESH-008.** La reutilización de un refresh token previamente rotado deberá revocar la familia completa.

## 13.3 Expiración

**FR-REFRESH-009.** Los refresh tokens deberán poseer una expiración absoluta configurable.

**FR-REFRESH-010.** El valor por defecto de expiración absoluta del refresh token deberá ser de 7 días, y deberá poder modificarse mediante configuración externa.

**FR-REFRESH-011.** Un usuario deshabilitado no deberá poder renovar una sesión.

**FR-REFRESH-012.** Un usuario bloqueado por Identity no deberá obtener un nuevo access token mediante refresh mientras el bloqueo permanezca activo.

## 13.4 Transporte en navegador

**FR-REFRESH-013.** Para el frontend Angular, el refresh token deberá transportarse mediante cookie con `HttpOnly`.

**FR-REFRESH-014.** En producción la cookie deberá marcarse `Secure`.

**FR-REFRESH-015.** En el despliegue same-origin definido por esta SRS, la cookie de refresh deberá utilizar `SameSite=Strict`.

**FR-REFRESH-016.** El access token no deberá persistirse obligatoriamente en `localStorage`.

La implementación recomendada será mantener el access token en memoria del frontend.

---

# 14. Renovación de sesión

El sistema deberá proporcionar conceptualmente:

```http
POST /api/auth/refresh
```

**FR-REFRESH-ENDPOINT-001.** El endpoint no deberá requerir un access token vigente.

**FR-REFRESH-ENDPOINT-002.** El endpoint deberá requerir un refresh token válido.

**FR-REFRESH-ENDPOINT-003.** Ante éxito deberá emitir un nuevo access token y rotar el refresh token.

**FR-REFRESH-ENDPOINT-004.** Ante refresh token inválido, vencido o revocado deberá responder `401 Unauthorized`.

**FR-REFRESH-ENDPOINT-005.** La respuesta no deberá revelar detalles que faciliten el análisis de tokens robados.

---

# 15. Logout

El sistema deberá proporcionar:

```http
POST /api/auth/logout
```

**FR-LOGOUT-001.** El logout deberá revocar la sesión o familia de refresh tokens correspondiente.

**FR-LOGOUT-002.** El logout deberá invalidar el refresh token mantenido por el navegador.

**FR-LOGOUT-003.** La cookie de refresh deberá eliminarse o invalidarse.

**FR-LOGOUT-004.** El logout deberá ser idempotente desde la perspectiva del cliente.

**FR-LOGOUT-005.** Los access tokens ya emitidos no deberán mantenerse en una blacklist central.

**FR-LOGOUT-006.** Un access token emitido antes del logout podrá continuar siendo criptográficamente válido hasta su expiración.

**FR-LOGOUT-007.** La duración corta del access token será el mecanismo de limitación de esa ventana residual.

---

# 16. Recuperación y restablecimiento de contraseña

## 16.1 Solicitud

El sistema deberá proporcionar:

```http
POST /api/auth/forgot-password
```

**FR-PWD-001.** El endpoint deberá aceptar un correo electrónico.

**FR-PWD-002.** El endpoint deberá admitir acceso anónimo.

**FR-PWD-003.** El endpoint no deberá revelar si el correo existe.

**FR-PWD-004.** La respuesta deberá ser externamente equivalente para cuentas existentes y no existentes.

**FR-PWD-005.** Para una cuenta existente y habilitada, el sistema deberá generar un token de restablecimiento mediante los mecanismos de ASP.NET Core Identity.

**FR-PWD-006.** El token de restablecimiento deberá enviarse mediante el proveedor de correo configurado.

## 16.2 Restablecimiento

El sistema deberá proporcionar:

```http
POST /api/auth/reset-password
```

**FR-PWD-007.** El request deberá incluir:

- correo electrónico;
- token de restablecimiento;
- nueva contraseña.

**FR-PWD-008.** El token deberá ser temporal.

**FR-PWD-009.** El token deberá invalidarse cuando su estado de seguridad asociado deje de ser válido.

**FR-PWD-010.** Un token alterado no deberá ser aceptado.

**FR-PWD-011.** Una nueva contraseña deberá cumplir la política configurada de Identity.

**FR-PWD-012.** Después de un reset exitoso deberán revocarse todas las familias de refresh tokens del usuario.

---

# 17. Cambio de contraseña autenticado

El sistema deberá proporcionar:

```http
POST /api/auth/change-password
```

**FR-CHANGE-PWD-001.** El endpoint deberá requerir autenticación.

**FR-CHANGE-PWD-002.** El usuario deberá proporcionar la contraseña actual y la nueva contraseña.

**FR-CHANGE-PWD-003.** La nueva contraseña deberá cumplir la política de Identity.

**FR-CHANGE-PWD-004.** Después de un cambio exitoso deberán revocarse las demás sesiones activas del usuario.

**FR-CHANGE-PWD-005.** No se exigirá una reautenticación adicional distinta del access token válido y de la comprobación de la contraseña actual definida por este endpoint.

Este endpoint permite reemplazar la contraseña inicial `admin` sin depender del envío de correo a la cuenta administrativa integrada.

---

# 18. Administración de usuarios

Todos los endpoints de esta sección deberán requerir autenticación y rol administrativo.

## 18.1 Crear usuario

Endpoint conceptual:

```http
POST /api/admin/users
```

**FR-USER-001.** El administrador deberá poder crear un usuario indicando al menos:

- correo electrónico;
- contraseña inicial;
- estado habilitado;
- roles iniciales opcionales.

**FR-USER-002.** El sistema deberá rechazar un correo normalizado duplicado.

**FR-USER-003.** La contraseña deberá cumplir la política de Identity.

## 18.2 Consultar usuarios

Endpoints conceptuales:

```http
GET /api/admin/users
GET /api/admin/users/{id}
```

**FR-USER-004.** El listado deberá permitir identificar como mínimo:

- id;
- email;
- estado habilitado;
- estado de lockout cuando resulte pertinente;
- roles.

**FR-USER-005.** La API nunca deberá exponer:

- password hash;
- refresh token completo;
- hash de refresh token;
- security stamp;
- token de reset;
- claves criptográficas.

## 18.3 Modificar usuario

Endpoint conceptual:

```http
PATCH /api/admin/users/{id}
```

**FR-USER-006.** El sistema sólo deberá permitir al administrador modificar los atributos explícitamente permitidos por el contrato.

**FR-USER-007.** Las modificaciones deberán validar unicidad de email.

## 18.4 Habilitar / deshabilitar

Endpoints conceptuales:

```http
POST /api/admin/users/{id}/enable
POST /api/admin/users/{id}/disable
```

**FR-USER-008.** Un usuario deshabilitado no podrá iniciar sesión.

**FR-USER-009.** Un usuario deshabilitado no podrá renovar sesiones.

**FR-USER-010.** Al deshabilitar una cuenta deberán revocarse todas sus familias de refresh tokens.

**FR-USER-011.** Habilitar una cuenta no deberá restaurar refresh tokens previamente revocados.

**FR-USER-012.** El sistema no deberá permitir deshabilitar al último administrador habilitado.

## 18.5 Revocar sesiones

Endpoint conceptual:

```http
POST /api/admin/users/{id}/revoke-sessions
```

**FR-USER-013.** Un administrador deberá poder revocar todas las sesiones renovables de un usuario.

**FR-USER-014.** La operación deberá afectar a todas las familias de refresh tokens activas.

## 18.6 Eliminación

**FR-USER-015.** La eliminación física rutinaria de usuarios queda fuera del alcance.

**FR-USER-016.** La operación administrativa equivalente a retirar acceso deberá realizarse mediante deshabilitación.

---

# 19. Autorización

**FR-AUTHZ-001.** Los endpoints `/api/admin/*` deberán requerir un access token válido.

**FR-AUTHZ-002.** Los endpoints `/api/admin/*` deberán exigir el rol administrativo.

**FR-AUTHZ-003.** Un request sin autenticación válida deberá producir `401 Unauthorized`.

**FR-AUTHZ-004.** Un usuario autenticado sin el rol requerido deberá producir `403 Forbidden`.

**FR-AUTHZ-005.** La autorización deberá utilizar claims incluidos en el JWT y políticas de ASP.NET Core.

---

# 20. Protección contra fuerza bruta y abuso

La protección deberá combinar controles por cuenta y por origen de red.

## 20.1 Lockout por cuenta

**NFR-SEC-BF-001.** ASP.NET Core Identity deberá contabilizar intentos fallidos de contraseña.

**NFR-SEC-BF-002.** La configuración inicial deberá bloquear una cuenta después de 5 intentos fallidos consecutivos.

**NFR-SEC-BF-003.** El lockout inicial deberá durar 15 minutos.

**NFR-SEC-BF-004.** Los valores anteriores deberán ser configurables sin recompilar.

**NFR-SEC-BF-005.** Un login exitoso deberá aplicar el comportamiento normal de Identity respecto de los contadores de acceso fallido.

## 20.2 Rate limiting por origen

**NFR-SEC-BF-006.** El sistema deberá aplicar rate limiting a los endpoints anónimos sensibles.

**NFR-SEC-BF-007.** Como mínimo deberán existir políticas diferenciables para:

- login;
- forgot-password;
- reset-password;
- refresh.

**NFR-SEC-BF-008.** El rate limiting deberá poder aplicarse por IP de origen.

**NFR-SEC-BF-009.** Cuando se alcance el límite correspondiente, la API deberá responder `429 Too Many Requests`.

## 20.3 Recuperación de contraseña

**NFR-SEC-BF-010.** La solicitud de recuperación deberá limitarse también por cuenta o correo normalizado, además del origen, cuando técnicamente sea posible sin introducir almacenamiento externo.

**NFR-SEC-BF-011.** El objetivo de esta protección será evitar abuso del servicio de correo contra una cuenta concreta.

## 20.4 Infraestructura

**NFR-SEC-BF-012.** El reverse proxy deberá permitir aplicar una primera capa de rate limiting antes de Authentication API.

**NFR-SEC-BF-013.** La protección implementada en infraestructura no sustituirá al lockout por cuenta de Identity.

---

# 21. Forwarded headers y dirección de origen

**NFR-NET-001.** Si Authentication API recibe tráfico detrás de un reverse proxy, deberá poder procesar forwarded headers necesarios para reconstruir la dirección y esquema originales.

**NFR-NET-002.** Authentication API solamente deberá confiar en forwarded headers procedentes de proxies o redes explícitamente autorizados.

**NFR-NET-003.** Los headers reenviados por un cliente no confiable no deberán permitir falsificar la identidad del proxy ni la IP utilizada por políticas de seguridad.

---

# 22. CORS y mismo origen

La arquitectura recomendada presenta Angular y las APIs bajo el mismo origen externo mediante reverse proxy.

**NFR-CORS-001.** Cuando frontend y API operen bajo el mismo origen, no deberá habilitarse CORS innecesariamente.

**NFR-CORS-002.** Si un despliegue alternativo requiere CORS, los orígenes permitidos deberán configurarse explícitamente.

**NFR-CORS-003.** No deberá utilizarse una política permisiva de cualquier origen en producción cuando se intercambien credenciales.

---

# 23. CSRF y cookies

**NFR-CSRF-001.** Las operaciones que dependan de cookies enviadas automáticamente por el navegador deberán considerar protección frente a CSRF.

**NFR-CSRF-002.** La política `SameSite` deberá ser compatible con el modelo same-origin y configurarse de forma restrictiva.

**NFR-CSRF-003.** Las solicitudes de navegador a refresh y logout deberán ser aceptadas únicamente desde el origen externo configurado para el frontend.

**NFR-CSRF-004.** La cookie de refresh no deberá ser accesible a JavaScript mediante `document.cookie`.

---

# 24. Enumeración de usuarios

**NFR-SEC-ENUM-001.** Login no deberá revelar si un correo existe.

**NFR-SEC-ENUM-002.** Forgot-password no deberá revelar si un correo existe.

**NFR-SEC-ENUM-003.** Reset-password no deberá proporcionar información innecesaria sobre la existencia de una cuenta.

**NFR-SEC-ENUM-004.** El flujo de login para una cuenta inexistente no deberá finalizar mediante una ruta temprana que omita el trabajo criptográfico equivalente a la verificación de una contraseña inválida; la implementación deberá utilizar una verificación de coste equivalente o un mecanismo técnicamente equivalente.

**NFR-SEC-ENUM-005.** Los logs internos deberán permitir distinguir las causas reales con fines operativos, sin exponer secretos.

---

# 25. Auditoría y logging

No se requiere un subsistema de auditoría independiente.

**NFR-LOG-001.** La aplicación deberá utilizar logging estructurado.

**NFR-LOG-002.** Como mínimo deberán poder registrarse eventos equivalentes a:

- login exitoso;
- login fallido;
- cuenta bloqueada;
- rate limit aplicado;
- logout;
- password reset exitoso;
- cambio de contraseña;
- usuario creado;
- usuario habilitado;
- usuario deshabilitado;
- rol asignado;
- rol retirado;
- reutilización de refresh token detectada;
- sesiones revocadas.

**NFR-LOG-003.** Los logs no deberán contener:

- contraseñas;
- access tokens completos;
- refresh tokens completos;
- reset tokens;
- claves privadas;
- secretos de configuración.

**NFR-LOG-004.** Los eventos deberán registrar timestamps en UTC.

**NFR-LOG-005.** Las solicitudes deberán poder correlacionarse mediante un identificador de correlación o mecanismo equivalente.

---

# 26. Persistencia SQLite

## 26.1 Base de datos

**CR-DATA-001.** Authentication API deberá utilizar un único archivo SQLite como persistencia.

**CR-DATA-002.** El archivo SQLite deberá residir fuera de la capa efímera del contenedor.

**CR-DATA-003.** La base de datos será propiedad exclusiva de Authentication API.

**CR-DATA-004.** Las APIs de negocio no deberán consultar directamente tablas de Identity ni tablas de refresh tokens.

**CR-DATA-005.** En producción, el directorio que contiene el archivo SQLite deberá montarse desde almacenamiento cuyo ciclo de vida sea independiente del proyecto Docker Compose.

**CR-DATA-006.** El despliegue de referencia deberá utilizar un bind mount hacia un directorio explícito del host para el archivo SQLite. Un volumen Docker marcado como `external` será una alternativa permitida si su ciclo de vida se administra fuera del proyecto Compose.

**CR-DATA-007.** La ejecución de `docker compose down -v` no deberá eliminar el archivo SQLite de producción.

**CR-DATA-008.** La ubicación persistente de la base deberá ser configurable y deberá estar documentada para el operador.

**CR-DATA-009.** La eliminación de la base de producción deberá requerir una acción explícita del operador sobre el almacenamiento persistente y no deberá ser consecuencia del teardown ordinario del stack Compose.

## 26.2 Modelo mínimo

El modelo persistente incluirá, directa o indirectamente mediante tablas estándar de Identity:

```text
User
Role
UserRole
RefreshToken / SessionFamily
```

Los detalles concretos del esquema generado por ASP.NET Core Identity y Entity Framework Core no forman parte del contrato externo.

## 26.3 Backup y restauración

**NFR-BACKUP-001.** La documentación operativa deberá incluir un procedimiento de backup del archivo SQLite.

**NFR-BACKUP-002.** La documentación operativa deberá incluir un procedimiento de restauración que permita reconstruir una instancia funcional de Authentication API a partir de un backup válido.

**NFR-BACKUP-003.** El procedimiento de backup y restauración no deberá requerir contenedores, bases de datos, schedulers ni servicios adicionales permanentes.

**NFR-BACKUP-004.** El procedimiento de backup deberá garantizar una copia consistente de SQLite mediante una operación soportada de backup de SQLite o mediante una ventana controlada en la que no existan escrituras concurrentes.

**NFR-BACKUP-005.** La restauración deberá preservar usuarios, roles, password hashes, sesiones persistidas y demás datos contenidos en la base al momento del backup.

**NFR-BACKUP-006.** Antes de declarar una release apta para producción deberá demostrarse al menos una restauración exitosa en un entorno descartable.

---

# 27. Inicialización y evolución del esquema

Este apartado fija expresamente el requisito de despliegue sin pasos manuales o etapas auxiliares.

## 27.1 Principio

**NFR-DB-INIT-001.** Ejecutar `docker compose up` deberá ser suficiente para que Authentication API pueda iniciar sobre un volumen vacío.

**NFR-DB-INIT-002.** Docker Compose no deberá ejecutar comandos `dotnet ef database update`.

**NFR-DB-INIT-003.** Docker Compose no deberá contener un servicio separado de migraciones.

**NFR-DB-INIT-004.** Docker Compose no deberá ejecutar scripts externos de bootstrap de la base.

**NFR-DB-INIT-005.** El operador no deberá ejecutar manualmente una etapa de inicialización de base de datos antes de iniciar Authentication API.

## 27.2 Autoinicialización de Authentication API

**NFR-DB-INIT-006.** Authentication API será responsable de preparar su almacenamiento durante su propio startup.

**NFR-DB-INIT-007.** Las migraciones de Entity Framework Core necesarias para la versión desplegada deberán estar incluidas en el artefacto ejecutable de Authentication API.

**NFR-DB-INIT-008.** Cuando existan migraciones pendientes, Authentication API deberá aplicarlas automáticamente antes de comenzar a aceptar tráfico normal.

**NFR-DB-INIT-009.** La aplicación deberá realizar esta operación de manera idempotente.

**NFR-DB-INIT-010.** Una base de datos ya actualizada no deberá ser recreada ni reinicializada.

**NFR-DB-INIT-011.** Después de disponer del esquema correcto, la aplicación deberá garantizar idempotentemente la existencia del rol administrativo y del usuario inicial definidos en la sección 8.

**NFR-DB-INIT-012.** La creación del esquema y del administrador formarán parte del proceso interno de startup de Authentication API y no constituirán una etapa externa de despliegue.

## 27.3 Fallos

**NFR-DB-INIT-013.** Si la actualización del esquema falla, Authentication API no deberá anunciarse como ready.

**NFR-DB-INIT-014.** El fallo deberá registrarse con información diagnóstica suficiente sin exponer secretos.

---

# 28. ASP.NET Core Data Protection

**NFR-DP-001.** Las claves de ASP.NET Core Data Protection deberán persistir fuera de la capa efímera del contenedor.

**NFR-DP-002.** Un restart, rebuild o recreación normal del contenedor no deberá invalidar tokens dependientes de Data Protection que continúen dentro de su período de validez por pérdida del key ring.

**NFR-DP-003.** El almacenamiento de Data Protection deberá ser accesible exclusivamente por Authentication API salvo necesidad técnica explícita.

**NFR-DP-004.** No se requerirá un servicio externo para almacenar estas claves.

**NFR-DP-005.** En producción, el key ring de Data Protection deberá almacenarse mediante bind mount o almacenamiento externo cuyo ciclo de vida sea independiente del proyecto Compose.

**NFR-DP-006.** La ejecución de `docker compose down -v` no deberá eliminar el key ring de Data Protection de producción.

**NFR-DP-007.** La ubicación del key ring deberá ser configurable y documentada.

---

# 29. Despliegue Docker Compose

## 29.1 Servicios

El deployment objetivo contendrá cuatro servicios funcionales:

```text
frontend
auth-api
api-a
api-b
```

El despliegue de referencia no deberá agregar:

```text
migration
bootstrap
redis
identity-server
vault
gateway
backup
```

como servicios adicionales.

## 29.2 Frontend / proxy

**NFR-DEPLOY-001.** El servicio que publica Angular deberá actuar como reverse proxy hacia Authentication API, Business API A y Business API B, evitando un servicio gateway adicional en el despliegue objetivo.

**NFR-DEPLOY-002.** Esta configuración deberá permitir que el navegador utilice un único origen externo.

## 29.3 Authentication API

**NFR-DEPLOY-003.** `auth-api` deberá pertenecer a la red interna de Compose.

**NFR-DEPLOY-004.** `auth-api` no deberá requerir publicar directamente su puerto al host en producción.

**NFR-DEPLOY-005.** `auth-api` deberá montar el directorio persistente de SQLite desde almacenamiento independiente del ciclo de vida del proyecto Compose.

**NFR-DEPLOY-006.** `auth-api` deberá montar el key ring de Data Protection desde almacenamiento independiente del ciclo de vida del proyecto Compose.

**NFR-DEPLOY-007.** `auth-api` deberá tener acceso al archivo de clave privada RSA.

## 29.4 APIs de negocio

**NFR-DEPLOY-008.** `api-a` y `api-b` deberán disponer de la clave pública RSA.

**NFR-DEPLOY-009.** Las APIs de negocio no deberán disponer de la clave privada.

## 29.5 Requisito de despliegue simple

**NFR-DEPLOY-010.** El despliegue normal no deberá requerir más acciones funcionales que suministrar configuración/secretos persistentes y ejecutar Docker Compose.

Ejemplo conceptual:

```bash
docker compose up -d
```

No se requerirá como paso de despliegue:

```bash
dotnet ef database update
```

ni un comando independiente para crear usuarios, roles o la base inicial.

**NFR-DEPLOY-011.** El teardown del stack mediante `docker compose down -v` no deberá eliminar la base SQLite, el key ring de Data Protection ni la clave privada RSA de producción.

**NFR-DEPLOY-012.** El Compose de producción no deberá declarar esos tres elementos críticos exclusivamente como named volumes administrados por el propio proyecto Compose.

**NFR-DEPLOY-013.** El despliegue deberá documentar de forma explícita las rutas o nombres externos utilizados para SQLite, Data Protection y la clave RSA.

**NFR-DEPLOY-014.** La estrategia de persistencia no deberá requerir un contenedor adicional de almacenamiento, migración, backup o inicialización.

---

## 29.6 Layout persistente de referencia

El despliegue de referencia deberá poder representarse mediante una estructura equivalente a:

```text
/srv/auth-system/
├── data/
│   └── auth.db
├── dataprotection/
│   └── ...
└── keys/
    └── jwt-private.pem
```

Estos paths representan almacenamiento del host y no directorios efímeros dentro del contenedor. Los nombres concretos serán configurables.

---

# 30. Configuración

**NFR-CONFIG-001.** Toda configuración dependiente del ambiente deberá suministrarse externamente al código.

La configuración deberá contemplar como mínimo:

```text
Database
JWT issuer
JWT audience
JWT access token lifetime
Refresh token lifetime
RSA private key path
RSA public key path
Identity lockout settings
Rate limit settings
Allowed proxy/network settings
Mail provider settings
Allowed origins when CORS is required
Data Protection key path
SQLite persistent host path
Backup/restore operational paths
```

**NFR-CONFIG-002.** Los secretos no deberán incorporarse a la imagen Docker.

**NFR-CONFIG-003.** Los secretos no deberán escribirse en logs.

---

# 31. Comunicación por correo

**NFR-MAIL-001.** El envío de correo deberá abstraerse mediante una interfaz o puerto de aplicación.

**NFR-MAIL-002.** El proveedor concreto no deberá formar parte de la lógica de negocio.

**NFR-MAIL-003.** SMTP será una implementación aceptable para el MVP.

**NFR-MAIL-004.** Las credenciales SMTP deberán configurarse externamente.

**NFR-MAIL-005.** Un fallo de envío deberá registrarse sin registrar el token de recuperación.

---

# 32. Health checks

El sistema deberá proporcionar health checks operativos.

## 32.1 Liveness

**NFR-HEALTH-001.** Deberá existir una comprobación que permita determinar que el proceso está vivo.

## 32.2 Readiness

**NFR-HEALTH-002.** Deberá existir una comprobación que permita determinar que el servicio está preparado para atender requests.

**NFR-HEALTH-003.** Readiness deberá considerar el acceso a SQLite.

**NFR-HEALTH-004.** El servicio no deberá marcarse ready si la inicialización o actualización de esquema falló.

## 32.3 Exposición

**NFR-HEALTH-005.** Los health checks detallados no deberán exponer información sensible hacia clientes no autorizados.

---

# 33. OpenAPI y documentación

**NFR-DOC-001.** Authentication API deberá producir un contrato OpenAPI.

**NFR-DOC-002.** OpenAPI deberá describir:

- endpoints;
- métodos HTTP;
- request bodies;
- response bodies;
- códigos de estado;
- autenticación Bearer cuando corresponda.

**NFR-DOC-003.** La interfaz interactiva de documentación deberá estar habilitada en desarrollo y deshabilitada o restringida a redes autorizadas en producción.

**NFR-DOC-004.** La interfaz interactiva no deberá exponerse indiscriminadamente fuera de las redes autorizadas en producción.

---

# 34. Manejo de errores

**NFR-ERR-001.** Las respuestas de error deberán utilizar una estructura consistente.

Se recomienda `ProblemDetails`.

**NFR-ERR-002.** No deberán exponerse stack traces en respuestas de producción.

**NFR-ERR-003.** No deberán exponerse detalles internos de Entity Framework, SQLite o Identity.

## 34.1 Códigos HTTP esperados

| Condición | Código |
|---|---:|
| Operación exitosa | 200 / 201 / 204 |
| Request inválido | 400 |
| Credencial inválida | 401 |
| Token inválido | 401 |
| Autenticado sin autorización | 403 |
| Recurso inexistente | 404 |
| Conflicto de estado o duplicado | 409 |
| Rate limit | 429 |
| Error interno | 500 |
| Servicio temporalmente no disponible | 503 |

---

# 35. Contrato mínimo de endpoints

La superficie inicial deberá permanecer acotada.

## 35.1 Autenticación

```text
POST   /api/auth/login
POST   /api/auth/refresh
POST   /api/auth/logout
POST   /api/auth/forgot-password
POST   /api/auth/reset-password
POST   /api/auth/change-password
```

## 35.2 Usuarios

```text
GET    /api/admin/users
GET    /api/admin/users/{id}
POST   /api/admin/users
PATCH  /api/admin/users/{id}
PUT    /api/admin/users/{id}/roles
POST   /api/admin/users/{id}/enable
POST   /api/admin/users/{id}/disable
POST   /api/admin/users/{id}/revoke-sessions
```

## 35.3 Roles

```text
GET    /api/admin/roles
POST   /api/admin/roles
PATCH  /api/admin/roles/{id}
DELETE /api/admin/roles/{id}
```

## 35.4 Operación

```text
GET    /health/live
GET    /health/ready
```

No se requiere un endpoint JWKS en esta versión.

---

# 36. Requisitos de rendimiento

El sistema no requiere optimización para cargas masivas.

**NFR-PERF-001.** La autenticación deberá priorizar seguridad sobre latencia, especialmente en hashing de contraseñas.

**NFR-PERF-002.** La validación normal de JWT en las APIs de negocio deberá ser local.

**NFR-PERF-003.** Authentication API no deberá encontrarse en la ruta crítica de cada request de negocio una vez emitido un access token.

**NFR-PERF-004.** El sistema deberá ser apto para una instalación pequeña o mediana utilizando SQLite sin requerir un motor de base de datos servidor.

---

# 37. Disponibilidad y desacoplamiento

**NFR-AVAIL-001.** Una indisponibilidad temporal de Authentication API no deberá impedir a las APIs de negocio validar access tokens aún válidos.

**NFR-AVAIL-002.** Durante dicha indisponibilidad no podrán realizarse operaciones que requieran Authentication API, incluyendo login y refresh.

**NFR-AVAIL-003.** La clave pública necesaria para verificar JWT deberá permanecer disponible localmente para las APIs de negocio.

---

# 38. Tiempo

**NFR-TIME-001.** Todos los timestamps internos relacionados con autenticación deberán registrarse en UTC.

**NFR-TIME-002.** Las expiraciones de access tokens y refresh tokens deberán evaluarse en UTC.

**NFR-TIME-003.** La tolerancia de reloj utilizada al validar JWT deberá ser explícita y consistente entre las APIs consumidoras.

**NFR-TIME-004.** No deberá utilizarse una tolerancia excesiva que prolongue materialmente la vida nominal de los tokens.

---

# 39. Reglas de seguridad

## 39.1 Contraseñas

**NFR-SEC-PWD-001.** Las contraseñas nunca deberán almacenarse en texto plano.

**NFR-SEC-PWD-002.** Las contraseñas nunca deberán incluirse en logs.

**NFR-SEC-PWD-003.** La política de contraseña deberá configurarse mediante ASP.NET Core Identity.

**NFR-SEC-PWD-004.** Las reglas concretas de complejidad deberán poder modificarse mediante configuración sin alterar el contrato de API.

## 39.2 Transporte

**NFR-SEC-TLS-001.** El acceso externo en producción deberá utilizar HTTPS.

**NFR-SEC-TLS-002.** En producción, TLS deberá terminar en el servidor web/reverse proxy que constituye el punto de entrada externo definido por esta SRS.

**NFR-SEC-TLS-003.** La red interna de Docker podrá utilizar HTTP cuando se encuentre correctamente aislada y el modelo de despliegue así lo determine.

## 39.3 Tokens

**NFR-SEC-TOK-001.** Los access tokens no deberán registrarse completos.

**NFR-SEC-TOK-002.** Los refresh tokens no deberán registrarse completos.

**NFR-SEC-TOK-003.** Los tokens de password reset no deberán registrarse.

**NFR-SEC-TOK-004.** El sistema deberá rechazar JWT con algoritmo inesperado.

---

# 40. Modelo conceptual de datos

```text
+-------------------------+
|          User           |
+-------------------------+
| Id                      |
| UserName                |
| Email                   |
| NormalizedEmail         |
| PasswordHash            |
| Enabled                 |
| SecurityStamp           |
| Identity lockout data   |
+------------+------------+
             |
             | N:M
             v
+-------------------------+
|        UserRole         |
+------------+------------+
             |
             v
+-------------------------+
|          Role           |
+-------------------------+
| Id                      |
| Name                    |
| NormalizedName          |
+-------------------------+


+-------------------------+
|      RefreshToken       |
+-------------------------+
| Id                      |
| UserId                  |
| TokenHash               |
| FamilyId                |
| CreatedAtUtc            |
| ExpiresAtUtc            |
| RevokedAtUtc            |
| ReplacedByTokenId       |
+------------+------------+
             |
             v
           User
```

Este diagrama es conceptual.

Las tablas estándar generadas por ASP.NET Core Identity podrán utilizar su estructura nativa.

---

# 41. Flujos principales

## 41.1 Primer despliegue

```text
Operator
   |
   | docker compose up
   v
Authentication API
   |
   | abre/crea SQLite
   | aplica migraciones embebidas pendientes
   | asegura role Administrator
   | asegura UserName admin
   | asegura Email admin@local.invalid
   | asigna Administrator
   v
READY
```

No existe un contenedor separado de migración.

No existe un comando de bootstrap manual.

## 41.2 Login

```text
Angular
   |
   | POST /api/auth/login
   v
Reverse Proxy
   |
   v
Auth API
   |
   | Identity valida email/password
   | lockout/rate limit
   |
   +---- fallo ----> 401 genérico
   |
   +---- éxito
          |
          | JWT RS256
          | refresh token
          v
Angular
```

## 41.3 Request a una API de negocio

```text
Angular
   |
   | Authorization: Bearer <JWT>
   v
Business API A
   |
   | valida firma con public key
   | valida issuer
   | valida audience
   | valida exp
   | aplica roles
   v
Recurso
```

No existe request de validación hacia Authentication API.

## 41.4 Refresh

```text
Browser
   |
   | HttpOnly refresh cookie
   v
Auth API
   |
   | verifica token
   | verifica user
   | revoca token usado
   | emite nuevo token
   | emite nuevo JWT
   v
Browser
```

## 41.5 Reuse detection

```text
RT-1 usado -> RT-2 emitido

atacante:
RT-1 nuevamente
   |
   v
reuse detectado
   |
   v
revocar familia completa
```

## 41.6 Logout

```text
Browser
   |
   | POST logout
   v
Auth API
   |
   | revoca familia/sesión
   | elimina cookie
   v
204
```

---

# 42. Casos de uso

## UC-01 — Iniciar sesión

**Actor principal:** usuario anónimo.  
**Precondición:** existe una cuenta habilitada.  
**Entrada:** email y password.

**Flujo principal:**

1. El usuario proporciona email y password.
2. La infraestructura aplica las políticas de origen.
3. Authentication API verifica la política de rate limiting.
4. Identity localiza la cuenta normalizada.
5. Identity verifica la contraseña.
6. Se comprueba que la cuenta esté habilitada y no bloqueada.
7. Se genera un access token.
8. Se crea una familia de refresh tokens.
9. Se emite el refresh token.
10. Se devuelve éxito.

**Alternativas:**

- credencial incorrecta -> `401`;
- cuenta inexistente -> `401`;
- cuenta bloqueada -> `401`;
- cuenta deshabilitada -> `401`;
- rate limit excedido -> `429`.

**Postcondición:** existe una sesión renovable.

---

## UC-02 — Renovar sesión

**Actor principal:** usuario con refresh token.

**Flujo:**

1. El navegador presenta el refresh token.
2. Authentication API verifica su hash, estado y expiración.
3. Se verifica el estado del usuario.
4. Se invalida el token presentado.
5. Se genera el siguiente refresh token de la familia.
6. Se genera un nuevo JWT.
7. Se actualiza la cookie.
8. Se devuelve el nuevo access token.

**Postcondición:** el refresh token anterior no puede reutilizarse.

---

## UC-03 — Cerrar sesión

**Actor:** usuario.

1. El usuario solicita logout.
2. Authentication API identifica la sesión renovable.
3. Revoca la sesión/familia.
4. Elimina la cookie.
5. Devuelve resultado idempotente.

---

## UC-04 — Solicitar recuperación de contraseña

**Actor:** usuario anónimo.

1. El usuario envía un correo.
2. Se aplican rate limits.
3. La API responde de forma genérica.
4. Si la cuenta existe y puede recuperarse, Identity genera un token.
5. Se solicita al proveedor de correo el envío de instrucciones.

---

## UC-05 — Restablecer contraseña

**Actor:** usuario con token de recuperación.

1. El usuario presenta email, token y nueva contraseña.
2. Identity valida token.
3. Identity valida política de contraseña.
4. Cambia la contraseña.
5. Se revocan sesiones renovables previas.
6. Se devuelve éxito.

---

## UC-06 — Crear usuario

**Actor:** administrador.

1. El administrador presenta JWT.
2. Se valida rol.
3. Envía datos del nuevo usuario.
4. Se verifica email único.
5. Identity crea la cuenta.
6. Se asignan roles solicitados.
7. Se devuelve el recurso creado.

---

## UC-07 — Deshabilitar usuario

**Actor:** administrador.

1. Se valida autorización.
2. Se verifica que la operación no deje el sistema sin administradores.
3. Se deshabilita la cuenta.
4. Se revocan todas sus sesiones.
5. Se registra el evento.

---

## UC-08 — Primer startup

**Actor:** plataforma de ejecución.

1. Authentication API inicia.
2. Obtiene configuración.
3. Abre el archivo SQLite.
4. Aplica automáticamente migraciones embebidas pendientes.
5. Asegura que exista `Administrator`.
6. Asegura que exista el usuario integrado `admin`.
7. Si es creado por primera vez, utiliza password `admin`.
8. Asigna `Administrator`.
9. Inicia los endpoints.
10. Readiness pasa a estado saludable.

---

# 43. Requisitos de testabilidad

No se fija un porcentaje arbitrario de cobertura.

Se exige cobertura automatizada de los comportamientos críticos.

## 43.1 Identidad y login

**TEST-001.** Login válido produce access token.

**TEST-002.** Contraseña incorrecta produce respuesta genérica.

**TEST-003.** Usuario inexistente produce respuesta equivalente.

**TEST-004.** Usuario deshabilitado no puede autenticarse.

**TEST-005.** La cantidad configurada de fallos activa lockout.

**TEST-006.** Un usuario bloqueado no puede autenticarse.

**TEST-007.** El administrador inicial existe después de una base vacía.

**TEST-008.** El administrador inicial tiene `UserName=admin`.

**TEST-009.** El administrador inicial tiene email `admin@local.invalid`.

**TEST-010.** La contraseña inicial `admin` funciona en primera creación.

**TEST-011.** Reiniciar no restaura una contraseña ya cambiada a `admin`.

## 43.2 JWT

**TEST-012.** JWT posee los claims obligatorios.

**TEST-013.** JWT está firmado con RS256.

**TEST-014.** Una firma incorrecta es rechazada.

**TEST-015.** Issuer incorrecto es rechazado.

**TEST-016.** Audience incorrecto es rechazado.

**TEST-017.** JWT expirado es rechazado.

**TEST-018.** API A puede validar usando solo la clave pública.

**TEST-019.** API B puede validar usando solo la clave pública.

## 43.3 Refresh

**TEST-020.** Refresh válido produce nuevo JWT.

**TEST-021.** El refresh utilizado queda inválido.

**TEST-022.** Un refresh expirado es rechazado.

**TEST-023.** La reutilización de un refresh rotado revoca la familia.

**TEST-024.** Usuario deshabilitado no puede hacer refresh.

**TEST-025.** Logout impide refresh posterior.

## 43.4 Passwords

**TEST-026.** Forgot-password no revela existencia de cuenta.

**TEST-027.** Token de reset válido permite cambiar contraseña.

**TEST-028.** Token de reset inválido es rechazado.

**TEST-029.** Reset de contraseña revoca sesiones previas.

**TEST-030.** Change-password exige autenticación.

## 43.5 Administración

**TEST-031.** Un administrador puede crear usuario.

**TEST-032.** Email duplicado es rechazado.

**TEST-033.** Un administrador puede asignar rol.

**TEST-034.** Un administrador puede retirar rol.

**TEST-035.** Un usuario no administrador recibe `403` en `/api/admin/*`.

**TEST-036.** El último administrador habilitado no puede quedar sin rol administrativo.

**TEST-037.** El último administrador habilitado no puede ser deshabilitado.

## 43.6 Base de datos / startup

**TEST-038.** Authentication API inicia correctamente con volumen SQLite vacío.

**TEST-039.** El startup crea/actualiza el esquema sin ejecutar comandos externos.

**TEST-040.** Reiniciar contra una base ya inicializada no duplica roles.

**TEST-041.** Reiniciar contra una base ya inicializada no duplica admin.

**TEST-042.** Reiniciar no sobrescribe cambios del admin.

**TEST-043.** Un error de migración impide readiness.

## 43.7 Seguridad

**TEST-044.** Login puede producir `429` al superar el rate limit.

**TEST-045.** Forgot-password puede producir `429`.

**TEST-046.** Passwords no aparecen en logs de integración.

**TEST-047.** Refresh tokens completos no aparecen en logs.

**TEST-048.** La clave privada no está disponible para API A.

**TEST-049.** La clave privada no está disponible para API B.

**TEST-050.** En un entorno descartable equivalente a producción, `docker compose down -v` no elimina el archivo SQLite persistente.

**TEST-051.** En el mismo escenario, `docker compose down -v` no elimina el key ring de Data Protection.

**TEST-052.** En el mismo escenario, `docker compose down -v` no elimina la clave privada RSA.

**TEST-053.** Después de `docker compose down -v` seguido de `docker compose up -d`, los usuarios y roles persistidos siguen disponibles.

**TEST-054.** Un backup válido de SQLite puede restaurarse en un entorno descartable y permite iniciar Authentication API conservando usuarios y roles.

**TEST-055.** El punto de entrada externo enruta `/auth/*`, `/api-a/*` y `/api-b/*` sin requerir exposición directa de los puertos backend.

---

# 44. Criterios de aceptación del sistema

La versión será aceptable cuando pueda demostrarse el siguiente escenario de extremo a extremo:

1. Se parte de un host con Docker, Docker Compose y los archivos/configuraciones necesarios.
2. No existe una base SQLite previa.
3. Se ejecuta `docker compose up`.
4. No se ejecuta ningún comando `dotnet ef` desde Compose.
5. No se inicia ningún servicio de migraciones.
6. No se ejecuta ningún script manual de bootstrap.
7. Authentication API prepara automáticamente su esquema.
8. Authentication API crea automáticamente el rol administrativo.
9. Authentication API crea automáticamente el usuario interno `admin`.
10. La cuenta integrada posee email `admin@local.invalid`.
11. La contraseña inicial es `admin`.
12. El administrador puede autenticarse utilizando el endpoint email/password.
13. El administrador puede cambiar su contraseña.
14. El administrador puede crear un usuario normal.
15. El usuario normal inicia sesión por correo.
16. Authentication API emite un JWT firmado RS256.
17. API A valida el JWT con la clave pública.
18. API B valida el JWT con la clave pública.
19. Ninguna de las APIs de negocio conoce la clave privada.
20. Ninguna API de negocio consulta Authentication API por cada request.
21. El usuario renueva su sesión mediante refresh token.
22. El refresh token se rota.
23. La reutilización de un refresh anterior es rechazada y revoca la familia.
24. Logout invalida la capacidad de renovar la sesión.
25. Forgot-password no revela si un correo existe.
26. Reset-password puede completar correctamente un cambio de contraseña.
27. Cinco fallos consecutivos, con la configuración inicial, producen lockout.
28. El origen también queda sujeto a rate limiting.
29. La base SQLite sobrevive a una recreación normal del contenedor.
30. Las claves de Data Protection sobreviven a una recreación normal del contenedor.
31. Reiniciar Authentication API no restaura la contraseña del administrador ni duplica datos iniciales.
32. En un entorno de aceptación descartable, `docker compose down -v` no elimina SQLite, Data Protection ni la clave privada RSA.
33. Levantar nuevamente el stack después de dicho teardown conserva las identidades y roles persistidos.
34. Se demuestra una restauración exitosa de SQLite desde un backup documentado.
35. El navegador accede a Authentication API y a las APIs de negocio únicamente a través del reverse proxy del servicio frontend en el despliegue de referencia.

---

# 45. Matriz de trazabilidad

| Objetivo | Requisitos asociados |
|---|---|
| Login mediante email | FR-ID-001 a FR-ID-005, FR-LOGIN-* |
| Admin inicial | FR-ADMIN-BOOT-* |
| Password inicial admin | FR-ADMIN-BOOT-003 |
| Administración de usuarios | FR-USER-* |
| Administración de roles | FR-ROLE-* |
| Protección de admin principal | FR-ROLE-014/015, FR-USER-012 |
| JWT | FR-JWT-* |
| Firma asimétrica simple | FR-KEY-* |
| Refresh tokens | FR-REFRESH-*, FR-REFRESH-ENDPOINT-* |
| Logout | FR-LOGOUT-* |
| Recuperación de contraseña | FR-PWD-* |
| Cambio de contraseña | FR-CHANGE-PWD-* |
| Fuerza bruta | NFR-SEC-BF-* |
| Anti-enumeración | NFR-SEC-ENUM-* |
| Reverse proxy / red | NFR-NET-*, NFR-DEPLOY-* |
| SQLite | CR-DATA-* |
| Deploy sin migración Compose | NFR-DB-INIT-* |
| Sin bootstrap externo | NFR-DB-INIT-* |
| Data Protection | NFR-DP-* |
| Docker Compose | NFR-DEPLOY-* |
| Logging | NFR-LOG-* |
| Health checks | NFR-HEALTH-* |
| Testabilidad | TEST-* |

---

# 46. Decisiones arquitectónicas explícitas

Para evitar ambigüedad, quedan fijadas las siguientes decisiones:

| Tema | Decisión |
|---|---|
| Login normal | Email + password |
| Username normal | No se usa para login |
| Admin integrado | `UserName=admin` |
| Email inicial admin | `admin@local.invalid` |
| Password inicial admin | `admin` |
| Creación admin | Automática, idempotente, dentro de Auth API |
| Registro público | No |
| Base de datos | SQLite |
| ORM | Entity Framework Core |
| Identidad | ASP.NET Core Identity |
| Actualización de esquema | Automática durante startup de Auth API |
| `dotnet ef` en Compose | No |
| Contenedor migration | No |
| Script de bootstrap | No |
| JWT | Sí |
| Firma | RS256 |
| Claves activas | Un par |
| Rotación automática | No |
| Rotación manual | Permitida |
| JWKS | No obligatorio |
| Validación JWT | Local en API A/B |
| Introspection | No |
| Access token | Corto, recomendado 15 min |
| Refresh token | Opaco, hash, rotación |
| Refresh browser | Cookie HttpOnly |
| Access token browser | Preferentemente memoria |
| Logout | Revoca sesión renovable |
| Blacklist JWT | No |
| Fuerza bruta | Proxy/app + Identity |
| Rate limit | Sí |
| Lockout Identity | Sí |
| MFA | No |
| Redis | No |
| Vault/KMS | No |
| Reverse proxy | Integrado en el servicio que publica Angular; no se agrega gateway separado |
| Servicios objetivo Compose | 4 |
| Persistencia producción | Bind mounts del host; volumen `external` permitido como alternativa |
| `docker compose down -v` | No debe eliminar DB, Data Protection ni clave RSA |
| Backup | Procedimiento documentado sin servicio adicional |

---

# 47. Exclusiones deliberadas para preservar simplicidad

El equipo deberá considerar cualquier incorporación de los siguientes elementos como un cambio de alcance que requiere revisar esta SRS:

- OAuth Authorization Server;
- OpenID Connect Provider;
- login social;
- MFA;
- permisos por recurso;
- administración de dispositivos;
- sesiones visibles por dispositivo;
- revocación inmediata distribuida de JWT;
- Redis;
- bus de mensajes;
- eventos de identidad distribuidos;
- JWKS obligatorio;
- rotación automática de claves;
- HSM;
- Vault;
- KMS;
- PostgreSQL;
- clustering de Authentication API con múltiples escritores SQLite;
- portal administrativo separado.

---

# 48. Riesgos conocidos y decisiones conscientes

## 48.1 Password inicial `admin`

La contraseña `admin` es intencionalmente débil.

Se acepta únicamente como mecanismo de primer acceso simple.

El riesgo se mitiga mediante:

- servicio no publicado directamente;
- reverse proxy;
- rate limiting;
- lockout por cuenta;
- documentación explícita de cambio inmediato;
- endpoint de cambio de contraseña;
- no restauración automática del valor `admin`.

## 48.2 Access tokens no revocados inmediatamente

Se acepta que un JWT previamente emitido permanezca válido hasta expirar.

Se mitiga mediante:

- access tokens de corta duración;
- revocación de refresh tokens;
- deshabilitación de renovación;
- ausencia de infraestructura distribuida adicional.

## 48.3 Una sola clave RSA

Se acepta un único par de claves para preservar sencillez.

La clave deberá:

- persistir;
- poder reemplazarse manualmente;
- mantenerse fuera del repositorio;
- estar restringida a Authentication API.

## 48.4 SQLite

SQLite es apropiado para el alcance previsto.

Si el sistema evoluciona hacia múltiples instancias concurrentes de Authentication API o una carga significativamente superior, deberá revisarse la decisión de persistencia.

## 48.5 Eliminación accidental de almacenamiento Docker

Los named volumes administrados por un proyecto Compose pueden eliminarse mediante `docker compose down -v`.

Para que una operación de teardown no destruya identidad ni material criptográfico persistente, la baseline exige que SQLite, Data Protection y la clave privada RSA de producción utilicen almacenamiento con ciclo de vida externo al proyecto Compose.

La opción de referencia es un bind mount hacia paths explícitos del host; un volumen `external` administrado fuera del Compose es una alternativa válida.

El backup sigue siendo necesario para cubrir eliminación manual del host, corrupción del almacenamiento o fallos físicos.

---

# 49. Calidad de requisitos y método de verificación

## 49.1 Criterios de calidad

La redacción de requisitos de esta baseline se controlará con los siguientes criterios, alineados con las prácticas de ISO/IEC/IEEE 29148:2018:

1. **Necesario:** responde a una necesidad, restricción o riesgo identificado.
2. **Apropiado:** se expresa al nivel de abstracción correcto para una SRS.
3. **No ambiguo:** evita términos subjetivos sin condición verificable.
4. **Completo:** contiene la información necesaria para interpretar la obligación.
5. **Singular:** expresa una obligación principal identificable.
6. **Factible:** es realizable con el contexto y las restricciones tecnológicas definidas.
7. **Verificable:** existe una forma objetiva de demostrar cumplimiento.
8. **Correcto y consistente:** no contradice otros requisitos de la baseline.
9. **Trazable:** dispone de identificador estable y puede relacionarse con objetivos, casos de uso, pruebas o decisiones.
10. **Modificable:** la estructura e identificación permiten cambios controlados sin depender de referencias informales.

## 49.2 Lenguaje normativo

Los términos `deberá` y `no deberá` expresan requisitos obligatorios.

Los términos `podrá` o expresiones equivalentes se utilizarán únicamente para declarar una capacidad opcional o una alternativa explícitamente permitida; no deberán utilizarse para requisitos cuya aceptación dependa de una decisión aún abierta.

Los valores operativos configurables deberán distinguir entre:

- **valor por defecto obligatorio para la baseline**; y
- **capacidad de configuración**.

Los términos subjetivos como `adecuado`, `razonable`, `rápido`, `seguro`, `cuando corresponda` o `preferentemente` no deberán utilizarse como único criterio de aceptación de un requisito.

## 49.3 Métodos de verificación

Los requisitos podrán verificarse mediante uno o más de los siguientes métodos:

| Código | Método | Uso |
|---|---|---|
| `T` | Test automatizado o de integración | Comportamiento funcional, seguridad, errores, persistencia. |
| `D` | Demostración | Flujos end-to-end, despliegue, backup/restore, operación. |
| `I` | Inspección | Compose, configuración, permisos, OpenAPI, ausencia de secretos. |
| `A` | Análisis | Propiedades arquitectónicas que requieran razonamiento o revisión técnica. |

Como regla general:

- requisitos `FR-*` -> `T`, y cuando corresponda `D`;
- requisitos `CR-*` -> `I` + `T`/`D` cuando exista comportamiento ejecutable;
- requisitos `NFR-SEC-*` -> `T` + `I`;
- requisitos `NFR-DEPLOY-*`, `NFR-DB-INIT-*`, `NFR-DP-*`, `NFR-BACKUP-*` -> `D` + `I`;
- requisitos de interoperabilidad -> `T`;
- exclusiones de alcance -> `I`.

## 49.4 Gate de calidad de requisitos

Antes de convertir una sección de la SRS en una feature de Spec-Kit deberá verificarse que:

- cada obligación normativa tenga identificador;
- no existan identificadores duplicados;
- no dependa de una fase futura para poder ser probada;
- sus precondiciones y excepciones relevantes estén declaradas;
- exista al menos un método de verificación viable;
- no introduzca una tecnología concreta salvo restricción deliberada;
- las interfaces externas afectadas estén identificadas;
- las reglas relacionadas sean consistentes entre sí.

---

# 50. Referencias

- ISO/IEC/IEEE 29148:2018, *Systems and software engineering — Life cycle processes — Requirements engineering*. Edición 2, revisada y confirmada por ISO en 2024 y vigente al 2026-10-06; existe una Edición 3 en estado DIS, todavía no publicada como International Standard.
- Microsoft ASP.NET Core Identity documentation.
- Microsoft ASP.NET Core authentication and JWT Bearer documentation.
- Microsoft ASP.NET Core Data Protection documentation.
- Microsoft ASP.NET Core rate limiting documentation.
- OWASP Authentication Cheat Sheet.
- OWASP Forgot Password Cheat Sheet.
- Docker Docs, `docker compose down` y especificación de volúmenes Compose (`external` y bind mounts).

---

# 51. Revisión de calidad de la SRS respecto de ISO/IEC/IEEE 29148

## 51.1 Alcance de la evaluación

Esta revisión evalúa la calidad del artefacto SRS y su preparación para desarrollo y verificación. No constituye certificación ISO ni demuestra por sí sola conformidad integral del proceso de ingeniería de requisitos de una organización.

ISO/IEC/IEEE 29148:2018 continúa siendo la edición publicada vigente al 2026-10-06. La Edición 3 se encuentra en desarrollo como Draft International Standard.

## 51.2 Resultado de la revisión

| Criterio | Estado | Evidencia en esta SRS |
|---|---|---|
| Alcance definido | Conforme | Secciones 1–3 y exclusiones explícitas. |
| Contexto e interfaces | Conforme | Topología, actores, APIs consumidoras, reverse proxy y red interna. |
| Identificación individual | Conforme | Requisitos con IDs estables por categoría. |
| Trazabilidad | Conforme | Matriz de trazabilidad y casos de uso. |
| Verificabilidad | Conforme con control continuo | Tests, criterios de aceptación y métodos `T/D/I/A`. |
| Consistencia | Conforme tras revisión | Login por email, admin inicial, JWT, refresh, despliegue y persistencia alineados. |
| Ambigüedad | Mejorada | Defaults fijados; condiciones CSRF, expiración y anti-enumeración explicitadas. |
| Factibilidad | Conforme para el alcance declarado | Stack y topología deliberadamente simples. |
| Gestión de interfaces | Conforme | Endpoints mínimos, códigos HTTP, integración A/B/frontend. |
| Restricciones de diseño | Conforme | ASP.NET Core Identity, SQLite, Compose y RS256 identificados como restricciones. |
| Operabilidad | Conforme | Health, logging, persistencia, backup/restore y startup idempotente. |
| Resiliencia de datos | Conforme tras enmienda v1.1 | Supervivencia a restart/rebuild y a `docker compose down -v`. |

## 51.3 Observaciones

La SRS se considera **bien alineada con criterios de calidad de requisitos de ISO/IEC/IEEE 29148:2018 para el tamaño y criticidad del producto**.

No se declara “certificada ISO”. Una conformidad formal del proceso requeriría, además del documento, evidencia organizacional de actividades de elicitation, validación con stakeholders, gestión de cambios, trazabilidad durante el ciclo de vida y verificación de los productos de trabajo.

La baseline 1.1 es suficientemente precisa para utilizarse como fuente normativa de `spec.md`, `plan.md`, `tasks.md` y checklists de GitHub Spec-Kit.

---

# 52. Estado de baseline

Esta SRS define el alcance funcional y técnico de la primera versión de Authentication API.

El objetivo de diseño es obtener un servicio:

- pequeño;
- predecible;
- autocontenido;
- fácil de integrar;
- fácil de desplegar;
- sin dependencias infraestructurales innecesarias;
- suficientemente robusto para actuar como autoridad de autenticación de dos APIs de negocio y un frontend Angular.

La baseline queda deliberadamente cerrada a las funciones aquí descritas. Todo cambio que modifique alcance, seguridad, persistencia, exposición de red o contratos externos deberá versionar esta SRS y actualizar su trazabilidad.
