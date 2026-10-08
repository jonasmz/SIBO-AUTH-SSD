# Technical Constraints — Authentication API

**Documento:** Restricciones técnicas y de implementación  
**Proyecto:** Authentication API  
**Versión:** 1.1  
**Fecha:** 2026-10-06  
**Documentos relacionados:** `SRS_Authentication_API_v1.1.md`, `ROADMAP_SPECKIT_AUTH_API_v1.1.md`  
**Ámbito:** decisiones tecnológicas, arquitectónicas y de desarrollo  
**Uso previsto:** fuente normativa complementaria para GitHub Spec-Kit

---

# 1. Propósito

Este documento fija las decisiones técnicas que deliberadamente no forman parte de la SRS.

La SRS define qué debe hacer el sistema y sus restricciones funcionales y no funcionales.

El roadmap define en qué orden deberá implementarse.

Este documento define cómo deberá estructurarse técnicamente la solución y qué tecnologías estarán permitidas como baseline.

Su objetivo principal es impedir que una implementación realizada mediante GitHub Spec-Kit introduzca decisiones arquitectónicas arbitrarias, dependencias innecesarias o componentes incompatibles con la simplicidad buscada para el producto.

---

# 2. Orden de precedencia

Ante una discrepancia entre documentos se aplicará el siguiente orden:

1. `SRS_Authentication_API_v1.1.md`
2. `TECHNICAL_CONSTRAINTS.md`
3. `ROADMAP_SPECKIT_AUTH_API_v1.1.md`
4. `spec.md` de la feature activa
5. `plan.md`
6. `tasks.md`

Una restricción técnica no podrá modificar un requisito funcional o de seguridad definido por la SRS.

---

# 3. Principios técnicos rectores

La solución deberá priorizar:

- simplicidad;
- mantenibilidad;
- testabilidad;
- bajo acoplamiento;
- mínima cantidad de dependencias externas;
- seguridad razonable para el alcance;
- despliegue sencillo;
- comportamiento predecible;
- ausencia de infraestructura especulativa.

Se aplicarán de forma explícita:

- SOLID;
- KISS;
- YAGNI;
- separación de responsabilidades;
- dependency inversion;
- encapsulamiento;
- fail fast para configuración inválida;
- composición explícita mediante Dependency Injection.

Los patrones de diseño deberán utilizarse únicamente cuando resuelvan un problema existente.

No deberá introducirse un patrón sólo para cumplir una preferencia arquitectónica o anticipar una necesidad futura.

---

# 4. Plataforma .NET

## 4.1 Runtime

La aplicación deberá utilizar:

```text
.NET 10
Target Framework: net10.0
```

.NET 10 será la única versión objetivo del backend.

No se requerirá multitargeting.

## 4.2 ASP.NET Core

La Web API deberá utilizar:

```text
ASP.NET Core 10
```

No se permitirá ASP.NET Framework clásico.

## 4.3 Lenguaje

Se utilizará:

```text
C# 14
```

Se utilizará la versión estable de C# asociada al SDK .NET 10.

No deberá utilizarse:

```text
LangVersion=preview
```

ni características preview del framework.

## 4.4 SDK

El repositorio deberá contener `global.json`.

`global.json` deberá:

- fijar un SDK .NET 10 probado por el proyecto;
- evitar cambio accidental a otro major;
- permitir actualizaciones deliberadas de patch/feature band dentro de .NET 10.

La versión exacta de patch deberá actualizarse conscientemente durante mantenimiento.

---

# 5. Arquitectura

## 5.1 Estilo

La arquitectura obligatoria será:

```text
Arquitectura Hexagonal
+
Vertical Slices por feature
```

La arquitectura hexagonal define las dependencias entre núcleo e infraestructura.

Las vertical slices organizan los casos de uso por capacidad funcional.

No deberán interpretarse como patrones competidores.

## 5.2 Proyectos

La solución deberá contener como baseline:

```text
src/
├── Authentication.Domain/
├── Authentication.Application/
├── Authentication.Infrastructure/
└── Authentication.Api/

tests/
├── Authentication.UnitTests/
└── Authentication.IntegrationTests/
```

Podrá incorporarse un proyecto adicional de testing exclusivamente si aparece una necesidad real y no puede resolverse razonablemente en los dos proyectos anteriores.

Mientras las APIs de negocio reales no existan en este repositorio, podrá incorporarse exclusivamente un proyecto de producto adicional, `src/ReferenceConsumer.Api`, como consumidor de referencia mínimo desplegado como `api-a` y `api-b`. Deberá ser una única aplicación Minimal API sin capas Domain/Application/Infrastructure, sin referencias a proyectos `Authentication.*`, sin persistencia ni capacidad de negocio, y solo validará JWT localmente con la clave pública. Su permanencia en el Compose final (Roadmap Phase 8) o su reemplazo por las APIs de negocio reales requiere decisión explícita. *(Enmienda 1.1, 2026-10-07; ver DEC-009 del Roadmap.)*

## 5.3 Dependencias permitidas

```text
Domain
  |
  +-- no depende de otros proyectos del producto

Application
  |
  +-- Domain

Infrastructure
  |
  +-- Application
  +-- Domain

Api
  |
  +-- Application
  +-- Infrastructure
```

Reglas:

- `Domain` no deberá referenciar ASP.NET Core.
- `Domain` no deberá referenciar Entity Framework Core.
- `Domain` no deberá referenciar ASP.NET Core Identity.
- `Domain` no deberá referenciar SQLite.
- `Domain` no deberá referenciar SMTP.
- `Application` no deberá depender de implementaciones de Infrastructure.
- `Infrastructure` implementará los puertos definidos por Application cuando dichos puertos sean necesarios.
- `Api` será composition root.

## 5.4 Dominio

El dominio deberá permanecer agnóstico de tecnologías.

Las reglas de negocio deberán expresarse en tipos y servicios de dominio cuando exista comportamiento de dominio real.

No deberá construirse un modelo de dominio artificial alrededor de clases de ASP.NET Core Identity.

Las clases concretas de Identity pertenecerán a Infrastructure.

## 5.5 Vertical slices

Las features deberán organizarse por capacidad.

Ejemplo conceptual:

```text
Authentication.Application/
└── Features/
    ├── Login/
    ├── Users/
    ├── Roles/
    ├── RefreshTokens/
    ├── ChangePassword/
    └── PasswordRecovery/
```

Cada slice contendrá únicamente los tipos que necesita.

No se deberá crear una jerarquía horizontal global de:

```text
Services/
Repositories/
Managers/
Helpers/
Dtos/
```

cuando ello disperse una misma feature sin aportar aislamiento real.

## 5.6 CQRS

No se utilizará una infraestructura CQRS dedicada.

Podrán existir conceptualmente comandos y consultas cuando ayuden a expresar un caso de uso, pero no se incorporará:

```text
MediatR
```

ni un message bus interno para ejecutar handlers.

Los endpoints podrán invocar directamente handlers/casos de uso registrados mediante Dependency Injection.

## 5.7 Repository pattern

No se deberá crear un `GenericRepository<T>`.

Entity Framework Core ya proporciona las primitivas necesarias para persistencia.

Se crearán puertos/repositories específicos únicamente cuando:

- exista una frontera hexagonal real;
- el caso de uso lo necesite;
- la abstracción oculte una dependencia tecnológica relevante.

---

# 6. Estilo HTTP

## 6.1 Minimal APIs

La API deberá implementarse mediante:

```text
ASP.NET Core Minimal APIs
```

No se utilizarán Controllers como mecanismo principal.

Motivos:

- menor ceremonia;
- buen encaje con vertical slices;
- metadatos OpenAPI directos;
- superficie pequeña;
- endpoints fácilmente agrupables por feature.

## 6.2 Organización

Cada endpoint deberá registrarse desde su feature.

`Program.cs` deberá limitarse principalmente a:

- configuración;
- registro de servicios;
- middleware;
- montaje de endpoints;
- arranque.

No deberá contener la lógica funcional de los endpoints.

## 6.3 Routing

Se utilizarán route groups para agrupar:

```text
/api/auth/*
/api/admin/*
/health/*
```

Los nombres y contratos públicos deberán respetar la SRS.

---

# 7. ASP.NET Core Identity

La gestión de usuarios, roles, contraseñas y lockout deberá utilizar:

```text
ASP.NET Core Identity 10.x
```

Paquete principal:

```text
Microsoft.AspNetCore.Identity.EntityFrameworkCore
```

La versión major deberá coincidir con .NET/EF Core:

```text
10.x
```

No deberá implementarse manualmente:

- password hashing;
- password verification;
- lockout;
- password reset token generation;
- role storage;
- security stamp behavior cubierto por Identity.

Se podrá crear un `ApplicationUser` derivado de Identity para atributos estrictamente necesarios, como el estado habilitado si la implementación lo requiere.

---

# 8. Entity Framework Core

## 8.1 Versión

Se utilizará:

```text
Entity Framework Core 10.x
```

Todos los paquetes EF Core deberán utilizar el mismo major y una versión patch compatible.

Paquetes esperados:

```text
Microsoft.EntityFrameworkCore.Sqlite
Microsoft.EntityFrameworkCore.Design
Microsoft.AspNetCore.Identity.EntityFrameworkCore
```

`Microsoft.EntityFrameworkCore.Design` será una dependencia de desarrollo y no deberá ser necesaria en la imagen final de runtime.

## 8.2 Migraciones

Las migraciones EF Core formarán parte del repositorio.

La Auth API aplicará las migraciones pendientes durante su startup de acuerdo con la SRS.

Docker Compose no ejecutará `dotnet ef`.

No existirá un migration container.

## 8.3 DbContext

Existirá un DbContext de infraestructura responsable de:

- esquema Identity;
- roles;
- refresh tokens/sesiones;
- demás persistencia exclusiva de Auth API.

No se dividirá la pequeña base en múltiples DbContexts salvo necesidad demostrada.

---

# 9. SQLite

## 9.1 Motor

El backend utilizará SQLite.

La versión mínima compatible será:

```text
SQLite 3.46.1
```

Esta versión corresponde al mínimo soportado por el provider oficial de EF Core actualmente definido para EF Core 10.

## 9.2 Provider

Se utilizará exclusivamente el provider oficial:

```text
Microsoft.EntityFrameworkCore.Sqlite 10.x
```

No se incorporará un provider SQLite alternativo.

## 9.3 Testing

Las pruebas de persistencia deberán utilizar SQLite real.

No se utilizará:

```text
Microsoft.EntityFrameworkCore.InMemory
```

como sustituto de SQLite para pruebas donde importe el comportamiento de persistencia, constraints, transacciones o consultas.

Para tests rápidos podrá utilizarse SQLite in-memory manteniendo la conexión abierta durante el test.

Para pruebas de migración, persistencia o restart se utilizará un archivo SQLite temporal.

---

# 10. JWT y criptografía

## 10.1 Access tokens

Se utilizarán JWT firmados mediante:

```text
RS256
```

Se utilizarán las bibliotecas Microsoft.IdentityModel utilizadas por ASP.NET Core.

No se implementará manualmente parsing, firma o validación criptográfica.

## 10.2 Claves

Baseline:

```text
1 clave privada RSA
1 clave pública RSA
```

La clave privada existirá únicamente en Auth API.

Las APIs de negocio recibirán únicamente la clave pública.

## 10.3 Formato

El material RSA deberá almacenarse en formato PEM salvo incompatibilidad justificada.

Ejemplo conceptual:

```text
jwt-private.pem
jwt-public.pem
```

## 10.4 Rotación

No se implementará:

- rotación automática;
- KMS;
- Vault;
- HSM;
- JWKS obligatorio.

El reemplazo será manual mediante configuración y redeploy según la SRS.

## 10.5 Primitivas criptográficas

Para generación de refresh tokens se utilizará:

```text
System.Security.Cryptography.RandomNumberGenerator
```

No se utilizarán generadores pseudoaleatorios generales para material de seguridad.

---

# 11. Tiempo

Para abstracción del reloj deberá utilizarse:

```text
System.TimeProvider
```

No deberá crearse un `IClock` propio salvo una limitación demostrable de `TimeProvider`.

Esto permitirá pruebas deterministas de:

- expiración JWT;
- refresh token expiration;
- lockout-related application logic;
- timestamps.

Todos los instantes persistidos por la aplicación deberán normalizarse a UTC.

---

# 12. OpenAPI

## 12.1 Generación

Se utilizará el soporte oficial de ASP.NET Core:

```text
Microsoft.AspNetCore.OpenApi
```

No se incorporará Swashbuckle ni NSwag para generar el documento.

## 12.2 Especificación

La baseline utilizará:

```text
OpenAPI 3.1
```

salvo incompatibilidad comprobada con un consumidor requerido.

## 12.3 Metadata

Cada endpoint deberá declarar metadatos suficientes para documentar:

- resumen;
- request;
- response;
- status codes;
- autorización;
- errores esperados.

Los XML documentation comments podrán utilizarse para enriquecer el documento.

---

# 13. Scalar

## 13.1 Tecnología

La UI documental deberá utilizar:

```text
Scalar.AspNetCore
```

con el documento generado por `Microsoft.AspNetCore.OpenApi`.

## 13.2 Modo de documentación

Scalar deberá configurarse como documentación de sólo lectura.

Como mínimo deberá:

- ocultar/deshabilitar `Test Request`;
- ocultar el API client;
- no persistir credenciales;
- no ofrecer ejecución interactiva de requests.

La UI se utilizará para:

- navegar endpoints;
- consultar modelos;
- consultar códigos de respuesta;
- consultar esquemas;
- leer contratos.

## 13.3 Exposición

Development:

```text
OpenAPI + Scalar habilitados
```

Production:

- OpenAPI/Scalar deberán permanecer deshabilitados externamente o restringidos por Nginx a redes autorizadas;
- nunca deberán exponerse indiscriminadamente a Internet.

---

# 14. Reverse proxy y frontend

## 14.1 Tecnología

Se utilizará:

```text
Nginx
```

## 14.2 Responsabilidad

El mismo contenedor que sirve los archivos estáticos compilados de Angular deberá actuar como reverse proxy.

No se agregará un quinto servicio gateway.

Topología:

```text
Browser
   |
   v
Nginx + Angular static files
   |
   +--> /auth/*  --> auth-api
   +--> /api-a/* --> api-a
   +--> /api-b/* --> api-b
```

## 14.3 Imagen

Se utilizará la imagen oficial de Nginx.

Baseline de desarrollo:

```text
nginx:stable-alpine
```

Para releases de producción deberá fijarse la imagen a una versión o digest probado por el proyecto.

No deberán utilizarse floating tags sin control en una release productiva.

## 14.4 Proxy headers

Nginx deberá reenviar como mínimo los headers requeridos para:

- host original;
- protocolo original;
- IP de origen.

Auth API sólo confiará en forwarded headers de la red/proxy autorizado.

---

# 15. Logging

## 15.1 API

Todo el código de aplicación deberá registrar mediante:

```text
Microsoft.Extensions.Logging
ILogger<T>
```

No se deberá acoplar lógica de negocio a una implementación concreta de logging.

## 15.2 Console logging

Se utilizará el provider oficial de consola de .NET.

Los logs de consola deberán ser aptos para:

```text
docker logs
```

## 15.3 File logging

ASP.NET Core no incluye un provider first-party genérico para archivos.

Para cumplir el requisito del proyecto sin introducir un framework externo se implementará un provider mínimo propio basado en:

```text
ILoggerProvider
ILogger
```

Nombre conceptual:

```text
PersistentFileLoggerProvider
```

No se incorporará Serilog, NLog u otro framework en la baseline.

## 15.4 Responsabilidades del file provider

El provider deberá:

- recibir los mismos eventos emitidos mediante `ILogger`;
- escribir a un directorio configurable;
- utilizar UTF-8;
- producir una línea por evento;
- incorporar timestamp UTC;
- incorporar nivel;
- incorporar categoría;
- incorporar EventId cuando exista;
- incorporar correlation/trace id cuando exista;
- registrar excepciones sin exponer secretos;
- ser thread-safe.

La escritura deberá desacoplarse razonablemente del request mediante una cola en memoria y un writer de background, evitando I/O bloqueante significativo dentro de `ILogger.Log`.

## 15.5 Rotación

La baseline utilizará rotación diaria por UTC.

Formato conceptual:

```text
auth-2026-10-06.log
auth-2026-10-07.log
```

Retención por defecto:

```text
30 días
```

La retención deberá ser configurable.

## 15.6 Persistencia

Directorio dentro del contenedor:

```text
/app/logs
```

Deberá montarse desde almacenamiento del host independiente del lifecycle de Compose.

Layout de referencia:

```text
/srv/auth-system/logs/
```

La ejecución de:

```text
docker compose down -v
```

no deberá eliminar estos archivos.

## 15.7 Datos prohibidos

No deberán registrarse:

- passwords;
- access tokens completos;
- refresh tokens;
- password reset tokens;
- private keys;
- SMTP password;
- secretos.

---

# 16. Email / SMTP

## 16.1 Abstracción

Application definirá un puerto mínimo para envío del correo requerido por password recovery.

Ejemplo conceptual:

```text
IEmailSender
```

Este puerto no deberá existir antes de la fase del roadmap que lo necesite.

## 16.2 Implementación

La implementación SMTP utilizará:

```text
MailKit
```

Motivo:

`System.Net.Mail.SmtpClient` no está recomendado por Microsoft para nuevo desarrollo.

MailKit se utilizará exclusivamente como adapter de Infrastructure.

Domain y Application no dependerán de MailKit.

## 16.3 Configuración

SMTP será configurable mediante:

- host;
- port;
- transport security;
- username;
- password;
- sender address;
- sender display name.

Los secretos deberán provenir de configuración externa.

---

# 17. Validación de entrada

No se incorporará FluentValidation en la baseline.

Se utilizarán:

- tipos de request pequeños;
- validación explícita de Application;
- validación nativa de ASP.NET Core cuando resulte adecuada;
- reglas de dominio en Domain.

Se evitará duplicar reglas entre endpoint, Application y Domain.

Las reglas de seguridad o invariantes deberán vivir en el nivel que sea dueño de ellas.

---

# 18. Manejo de errores

Se utilizará:

```text
ProblemDetails
```

como representación estándar de errores HTTP.

No se creará un formato paralelo salvo requisito nuevo de la SRS.

La traducción de errores deberá ocurrir en el límite HTTP.

Domain no deberá conocer:

- códigos HTTP;
- ProblemDetails;
- ASP.NET Core.

---

# 19. Dependency Injection

Se utilizará el contenedor de Dependency Injection incluido en ASP.NET Core.

No se incorporará:

- Autofac;
- Lamar;
- SimpleInjector;
- otro container externo.

Los registros deberán organizarse mediante métodos de extensión por capa o feature cuando ayude a mantener `Program.cs` pequeño.

---

# 20. Mapeo

No se utilizará AutoMapper en la baseline.

Los mappings pequeños deberán escribirse de forma explícita.

La introducción futura de una librería de mapping requerirá evidencia de repetición significativa que justifique la dependencia.

---

# 21. Framework de testing

## 21.1 Framework

Se utilizará:

```text
xUnit.net v3
```

## 21.2 Runner

Se utilizará:

```text
Microsoft Testing Platform
```

mediante la integración oficial de xUnit v3.

El repositorio deberá permitir:

```text
dotnet test
```

desde .NET 10.

## 21.3 Assertions

Se utilizarán las assertions incluidas con xUnit v3:

```text
Assert.*
```

No se incorporará FluentAssertions como dependencia base.

No se requiere una librería adicional de assertions para el alcance del producto.

---

# 22. Testing auxiliar

## 22.1 Integration testing

Se utilizará:

```text
Microsoft.AspNetCore.Mvc.Testing 10.x
WebApplicationFactory<TEntryPoint>
```

para integration/functional tests de la API.

Aunque el producto use Minimal APIs, `WebApplicationFactory` seguirá siendo la infraestructura de test HTTP.

## 22.2 Mocking

No se utilizará un mocking framework por defecto.

Preferencias:

1. objetos reales;
2. fakes simples;
3. stubs explícitos;
4. mocks sólo cuando una interacción sea la conducta que realmente se desea probar.

Si un caso concreto justifica un framework de mocking, la opción permitida será:

```text
NSubstitute
```

pero su introducción deberá ser puntual y justificada.

No deberá agregarse preventivamente al proyecto.

## 22.3 Base de datos

Las pruebas deberán utilizar:

- SQLite in-memory para escenarios rápidos donde se requiera SQL real;
- archivo SQLite temporal para migraciones, restart y persistencia;
- nunca EF Core InMemory para demostrar comportamiento SQLite.

## 22.4 Email

Las pruebas de Application utilizarán un fake de `IEmailSender`.

Los tests SMTP reales deberán limitarse a integración cuando exista un servidor SMTP de prueba configurado.

No se requerirá un SMTP externo para ejecutar la suite normal.

## 22.5 Tiempo

Los tests deberán utilizar `FakeTimeProvider` o un `TimeProvider` controlado cuando necesiten modificar el tiempo.

No se utilizarán sleeps reales para probar expiraciones.

---

# 23. Estrategia de pruebas

Se evitará perseguir un porcentaje arbitrario de code coverage.

La prioridad será:

```text
reglas críticas
+
seguridad
+
contratos HTTP
+
persistencia
+
integración entre servicios
```

Tipos:

### Unit tests

Para:

- reglas de dominio;
- handlers de Application;
- cálculos de expiración;
- decisiones de estado;
- reglas del último administrador;
- refresh token family behavior.

### Integration tests

Para:

- Identity;
- EF Core;
- SQLite;
- migrations;
- Minimal API endpoints;
- JWT;
- authorization;
- ProblemDetails;
- Data Protection;
- file logging cuando corresponda.

### Deployment / acceptance tests

Para:

- Compose;
- Nginx routing;
- persistencia host-mounted;
- `docker compose down -v`;
- backup/restore;
- integración API A/B.

---

# 24. Analyzers y calidad de compilación

Se utilizarán los analyzers incluidos en el SDK de .NET.

No se agregará StyleCop, Roslynator u otro paquete de analyzers como baseline.

El repositorio deberá configurar:

```text
Nullable = enable
ImplicitUsings = enable
EnforceCodeStyleInBuild = true
```

Los warnings producidos por código first-party deberán corregirse.

No se permitirán suppressions globales sin justificación documentada.

`.editorconfig` será la fuente de reglas de estilo automatizables.

---

# 25. Convenciones C#

Se seguirán las convenciones estándar de Microsoft para C# y .NET.

Reglas adicionales del proyecto:

## 25.1 Archivos y tipos

Deberá existir:

```text
un tipo top-level declarado por archivo
```

El nombre del archivo deberá coincidir con el tipo.

Ejemplos:

```text
LoginRequest.cs       -> LoginRequest
LoginHandler.cs       -> LoginHandler
RefreshToken.cs       -> RefreshToken
```

Excepciones:

- `Program.cs`;
- tipos privados o nested cuyo único propósito sea encapsular implementación local;
- código generado.

No deberán agruparse varios records públicos en un único archivo.

## 25.2 Naming

- tipos y miembros públicos: `PascalCase`;
- parámetros y variables locales: `camelCase`;
- interfaces: prefijo `I`;
- métodos async: sufijo `Async` cuando devuelvan `Task`/`ValueTask`, salvo convenciones framework que justifiquen lo contrario;
- constantes: `PascalCase` según convenciones .NET;
- namespaces alineados con proyecto/feature.

## 25.3 Namespaces

Se utilizarán file-scoped namespaces.

## 25.4 Nullable

Nullable reference types permanecerá habilitado.

No deberán utilizarse operadores `!` para silenciar problemas salvo invariantes técnicamente demostrables.

---

# 26. Diseño y mantenibilidad

## 26.1 SOLID

SOLID será una guía y no un objetivo de maximización de interfaces.

En particular:

- SRP no implica una clase por método;
- DIP no implica abstraer todas las clases;
- ISP no implica dividir interfaces sin consumidores reales.

## 26.2 KISS

Ante dos soluciones funcionalmente equivalentes se preferirá la de:

- menor número de tipos;
- menor número de dependencias;
- menor configuración;
- menor infraestructura.

## 26.3 YAGNI

No se implementará funcionalidad futura anticipadamente.

Ejemplos:

- JWKS antes de necesitarlo;
- key rotation service;
- event bus;
- generic repository;
- caching layer;
- user device tracking;
- OAuth abstractions;
- tenant abstraction.

## 26.4 Design patterns permitidos

Se utilizarán cuando exista necesidad:

- Ports and Adapters;
- Strategy;
- Factory;
- Adapter;
- Decorator;
- Result/typed outcome;
- Specification sólo si existe una regla combinatoria suficientemente compleja.

No se deberá implementar un catálogo de patrones por anticipación.

---

# 27. Resultados y errores internos

Los casos de uso podrán devolver resultados tipados para representar:

- éxito;
- not found;
- conflict;
- forbidden business state;
- invalid input.

No se utilizarán excepciones como mecanismo normal de control de flujo.

Las excepciones deberán reservarse para errores excepcionales o fallos de infraestructura.

Api traducirá esos resultados a HTTP.

---

# 28. Docker — Auth API

## 28.1 Imágenes oficiales

Se utilizarán imágenes oficiales Microsoft.

Build:

```text
mcr.microsoft.com/dotnet/sdk:10.0
```

Runtime:

```text
mcr.microsoft.com/dotnet/aspnet:10.0
```

## 28.2 Multi-stage build

El Dockerfile deberá utilizar multi-stage build.

Etapas mínimas conceptuales:

```text
restore
build/publish
runtime
```

La imagen final:

- no contendrá el SDK;
- no contendrá `dotnet-ef`;
- no contendrá código fuente innecesario;
- no contendrá secretos.

## 28.3 Distribución base

Se utilizará la distribución Linux provista por la imagen oficial seleccionada.

No se utilizará Alpine o chiseled como requisito inicial para Auth API.

Motivo:

priorizar compatibilidad y capacidad de diagnóstico sobre la mínima reducción de tamaño.

Una optimización a chiseled podrá evaluarse posteriormente si no altera operación o diagnóstico.

## 28.4 Pinning

Durante desarrollo podrá utilizarse el tag de major:

```text
10.0
```

Para una release de producción se deberá registrar el digest o tag exacto validado.

## 28.5 Usuario

La imagen final deberá ejecutar la aplicación como usuario no-root.

Los bind mounts persistentes del host deberán tener permisos compatibles con el UID/GID de runtime.

---

# 29. Docker Compose

El Compose de referencia contendrá funcionalmente:

```text
frontend
auth-api
api-a
api-b
```

No se incorporará:

- migration service;
- bootstrap service;
- Redis;
- Vault;
- database server;
- gateway adicional;
- log collector obligatorio.

La aplicación deberá ser compatible con:

```text
docker compose up -d
```

sin pasos manuales de migración.

---

# 30. Persistencia fuera del lifecycle de Compose

En producción deberán residir fuera del lifecycle de los volúmenes administrados por Compose:

```text
SQLite database
Data Protection key ring
RSA private key
persistent log files
```

Layout de referencia:

```text
/srv/auth-system/
├── data/
│   └── auth.db
├── dataprotection/
├── keys/
│   ├── jwt-private.pem
│   └── jwt-public.pem
└── logs/
```

El mecanismo de referencia será:

```text
bind mounts
```

Un volumen Docker `external` será aceptable como alternativa para datos cuando esté administrado explícitamente fuera del proyecto.

`docker compose down -v` no deberá eliminar dichos artefactos.

---

# 31. Secrets y configuración

La configuración deberá utilizar el configuration system nativo de .NET:

- `appsettings.json`;
- `appsettings.{Environment}.json`;
- environment variables;
- archivos montados para claves/secretos cuando corresponda.

No se instalará un secrets manager adicional.

En desarrollo podrá utilizarse `.NET User Secrets` fuera de Docker cuando sea conveniente.

En producción los secretos no deberán incorporarse:

- al repositorio;
- al Dockerfile;
- a la imagen;
- a `appsettings.json` versionado.

---

# 32. Package management

Las versiones NuGet deberán centralizarse mediante:

```text
Directory.Packages.props
```

No se utilizarán rangos flotantes.

Ejemplos prohibidos:

```text
Version="10.*"
Version="*"
```

La baseline fijará una versión concreta probada de cada package.

Todos los paquetes Microsoft del stack deberán mantener majors compatibles:

```text
ASP.NET Core 10
EF Core 10
Identity 10
Mvc.Testing 10
```

---

# 33. Dependencias externas autorizadas

Baseline de dependencias no incluidas en el shared framework:

| Dependencia | Uso |
|---|---|
| `Microsoft.EntityFrameworkCore.Sqlite` 10.x | Persistencia SQLite |
| `Microsoft.EntityFrameworkCore.Design` 10.x | Migraciones en desarrollo |
| `Microsoft.AspNetCore.Identity.EntityFrameworkCore` 10.x | Identity + EF |
| `Microsoft.AspNetCore.Authentication.JwtBearer` 10.x | Validación JWT |
| `Microsoft.AspNetCore.OpenApi` 10.x | OpenAPI |
| `Scalar.AspNetCore` | UI documental read-only |
| `MailKit` | Adapter SMTP |
| `xUnit.net v3` | Tests |
| `Microsoft.AspNetCore.Mvc.Testing` 10.x | Integration tests |

`NSubstitute` no formará parte de la baseline inicial; queda permitido sólo si un test concreto lo justifica.

Cualquier nueva dependencia deberá responder a una necesidad actual y documentarse en el plan de la feature que la introduce.

---

# 34. Dependencias explícitamente no deseadas

Salvo cambio técnico aprobado, no utilizar:

```text
MediatR
AutoMapper
FluentValidation
FluentAssertions
Serilog
NLog
Swashbuckle
NSwag
EntityFrameworkCore.InMemory
GenericRepository packages
Autofac
Redis
MassTransit
RabbitMQ
Kafka
Hangfire
Quartz
Vault clients
OpenIddict
Duende IdentityServer
```

La exclusión no implica que sean tecnologías defectuosas.

No son necesarias para el alcance de este producto.

---

# 35. Observabilidad

La baseline no incorporará una plataforma externa de observabilidad.

Se utilizarán:

- console logs;
- persistent text logs;
- health endpoints;
- correlation/trace identifiers;
- métricas operativas sólo si aparece una necesidad concreta.

No se instalará inicialmente:

- Prometheus;
- Grafana;
- OpenTelemetry Collector;
- Seq;
- ELK;
- Application Insights.

La incorporación futura requerirá cambio explícito.

---

# 36. Correlation y tracing

Se aprovechará `System.Diagnostics.Activity` y la infraestructura nativa ASP.NET Core.

No deberá inventarse un tracing framework propio.

Los logs deberán incluir cuando estén disponibles:

- TraceId;
- SpanId;
- correlation identifier.

Si el reverse proxy suministra un correlation id válido, podrá preservarse; de lo contrario la aplicación deberá disponer de uno para correlación local.

---

# 37. Nginx y límites de responsabilidad

Nginx deberá encargarse de:

- servir Angular;
- reverse proxy;
- TLS termination en producción;
- primera capa de rate limiting;
- tamaño máximo de request;
- forwarded headers;
- routing.

Auth API continuará encargándose de:

- Identity lockout;
- rate limiting de aplicación;
- autorización;
- validación de origen relevante;
- seguridad funcional.

Una capa no sustituirá a la otra.

---

# 38. Seguridad de archivos persistentes

Los siguientes archivos/directorios deberán tener acceso restringido:

```text
/srv/auth-system/data
/srv/auth-system/dataprotection
/srv/auth-system/keys
/srv/auth-system/logs
```

La clave privada RSA deberá ser legible únicamente por el proceso/usuario autorizado de Auth API.

El archivo SQLite y Data Protection deberán permitir escritura únicamente a Auth API y al operador autorizado.

Los logs deberán ser escribibles por Auth API y protegidos frente a acceso no autorizado.

---

# 39. Backup

No se añadirá un servicio de backup.

La documentación operativa deberá utilizar capacidades simples de SQLite y del host.

El mecanismo deberá producir un backup consistente mediante:

- SQLite backup API/command compatible; o
- parada controlada de escrituras durante copia.

La restauración deberá probarse según la SRS y el roadmap.

---

# 40. Reglas para GitHub Spec-Kit

Cada `plan.md` deberá respetar este documento.

Durante `/speckit.analyze` deberá marcarse como problema cualquier propuesta que:

- cambie el stack;
- introduzca una dependencia no autorizada sin justificación;
- rompa las dependencias hexagonales;
- introduzca arquitectura especulativa;
- agregue componentes de infraestructura innecesarios;
- cambie Minimal APIs por Controllers;
- agregue migration/bootstrap containers;
- reintroduzca EF InMemory en tests de persistencia;
- agregue JWT blacklist;
- agregue key rotation automática.

Cada fase sólo deberá introducir las dependencias necesarias para esa fase.

Ejemplo:

```text
MailKit
```

no deberá agregarse antes de la fase de password recovery.

---

# 41. Definition of Done técnica

Una feature no se considerará completa si:

- viola las dependencias de proyectos;
- introduce warnings nuevos de código first-party;
- introduce package nuevo sin justificación;
- no tiene pruebas correspondientes;
- deja secretos en código/configuración versionada;
- introduce tipos futuros no utilizados;
- rompe `dotnet build`;
- rompe `dotnet test`;
- requiere un paso manual no documentado.

---

# 42. Matriz tecnológica resumida

| Área | Decisión |
|---|---|
| Runtime | .NET 10 |
| Lenguaje | C# 14 estable |
| API | ASP.NET Core 10 Minimal APIs |
| Arquitectura | Hexagonal + Vertical Slices |
| Domain | Agnóstico de tecnología |
| Identity | ASP.NET Core Identity 10.x |
| ORM | EF Core 10.x |
| Database | SQLite >= 3.46.1 |
| Provider DB | Microsoft.EntityFrameworkCore.Sqlite |
| JWT | RS256 |
| RNG | System.Security.Cryptography |
| Clock | System.TimeProvider |
| OpenAPI | Microsoft.AspNetCore.OpenApi |
| OpenAPI version | 3.1 |
| API docs | Scalar.AspNetCore read-only |
| Reverse proxy | Nginx |
| Logging API | Microsoft.Extensions.Logging |
| Console logs | Built-in Console provider |
| File logs | Custom minimal ILoggerProvider |
| File log retention | 30 días por defecto |
| SMTP | MailKit |
| Test framework | xUnit.net v3 |
| Test runner | Microsoft Testing Platform |
| HTTP integration tests | Microsoft.AspNetCore.Mvc.Testing |
| Assertions | xUnit Assert |
| Mocking | Fakes; NSubstitute sólo si se justifica |
| DB tests | SQLite real |
| DI | ASP.NET Core built-in DI |
| Error contract | ProblemDetails |
| Mapping | Manual/explícito |
| Container build | Multi-stage |
| Build image | mcr.microsoft.com/dotnet/sdk:10.0 |
| Runtime image | mcr.microsoft.com/dotnet/aspnet:10.0 |
| Runtime user | non-root |
| Frontend/proxy image | nginx:stable-alpine en desarrollo |
| Persistent storage | Host bind mounts |
| Compose services | 4 |
| Migration service | No |
| Bootstrap service | No |
| Redis | No |
| Vault/KMS | No |
| MediatR | No |
| AutoMapper | No |
| Serilog/NLog | No |
| FluentValidation | No |
| Swashbuckle/NSwag | No |

---

# 43. Referencias técnicas verificadas

Esta baseline fue contrastada al 2026-10-06 con documentación vigente de:

- Microsoft .NET 10 y ASP.NET Core 10;
- Microsoft EF Core SQLite provider;
- Microsoft ASP.NET Core OpenAPI;
- Microsoft ASP.NET Core integration testing;
- Microsoft.Extensions.Logging;
- xUnit.net v3;
- Scalar ASP.NET Core;
- documentación Microsoft de `System.Net.Mail.SmtpClient`.

Datos relevantes verificados:

- .NET 10 es LTS y utiliza C# 14 como versión de lenguaje asociada.
- `Microsoft.EntityFrameworkCore.Sqlite` soporta SQLite 3.46.1 en adelante.
- ASP.NET Core 10 genera OpenAPI 3.1 mediante `Microsoft.AspNetCore.OpenApi`.
- `WebApplicationFactory<TEntryPoint>` continúa siendo la infraestructura oficial de integration testing.
- ASP.NET Core no incluye un provider genérico built-in para logs a archivo.
- `ILoggerProvider` es la extensión nativa para implementar providers adicionales.
- Scalar permite ocultar el API client y el botón `Test Request`, permitiendo una referencia documental no interactiva.
- Microsoft no recomienda `System.Net.Mail.SmtpClient` para nuevo desarrollo y señala MailKit como alternativa.

---

# 44. Baseline

Este documento fija la baseline técnica inicial del proyecto.

El agente de implementación no deberá reemplazar una tecnología aquí definida por otra "equivalente" sin que exista una modificación explícita de este documento.

La intención no es maximizar la cantidad de patrones o abstracciones, sino producir una Authentication API pequeña, segura, verificable, mantenible y sencilla de integrar en el Compose definido por la SRS.
