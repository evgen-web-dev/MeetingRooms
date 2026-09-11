# What decided and what is not yet

Both "Decided" and "Not yet decided" list items are not in "linear" or chronological order, they are just a sequence of overall decisions for project in general.

## Decided
These are decided points, but if there are points which do not fit current project's scope / implementation plan; or if some points are in conflict with task description from `assignment.md` - let's discuss them:

- Single Web App, frontend served from wwwroot (no CORS, no cross-origin SignalR negotiate);
- Clean Architecture approach, with maintaining Clean Architecture's linkage and references between projects/layers:
    Domain         → (nothing)
    Application    → Domain
    Infrastructure → Application + Domain
    Api            → Application + Infrastructure

- Controllers for endpoints (say `AuthController : ControllerBase`);
- Global exception handler;
- Extension methods for registering services / adding configurations - to not make Program.cs giant;
- Scalar as OpenAPI debugging tool;
- JWT access token only, no refresh tokens;
- ASP .NET Identity for managing users;
- SQL Server (Azure SQL Database) as DB;
- FluentValidation for validation;
- FluentValidation validators are all being applied in one place - middleware, not per endpoint;
- Addressing errors with Result pattern;
- Always putting DbContext (AppDbContext) behind UnitOfWork / IRepository, never using it directly (seeders may be exceptions for using DbContext directly);
- Using ASP .NET Identity's UserManager / RoleManager for working with roles / users - but not directly, but via abstractions like IUserIdentityService / IRoleIdentityService;
- Errors for business rules are returned as error-codes, like "InvalidEmailOrPassword"; validation errors can be text-errors;
- Wrapping business errors / exceptions caught in global exception handler with ProblemDetails; wrapping validation errors with ValidationProblemDetails;
- Sensitive errors, like error-codes from ASP .NET Identity's errors: DuplicateUserName / DuplicateEmail / InvalidUserName, must not be leaked verbatim (say from Infrastructure layer to Application layer); one of possible approaches is to fold/map such errors into more genereic ones like InvalidEmailOrUserName;
- Using IEntityTypeConfiguration<TEntity> + (modelBuilder.ApplyConfigurationsFromAssembly in DbContext (AppDbContext));
- Using -Request / -Response naming for DTOs (where possible) - CreateUserRequest, CreateBookingResponse, etc;
- Create IOptions for important options, like for JWT-options, with validation of each option that fails loudly;
- React + Typescript + Tailwind for front-end;

## Not yet decided
These are decided points for you (Claude) to decide / for us to discuss together and then decide:

- Playwright for auto-test?
- Approach for booking concurrency, needs to be decided on the future assumed/predicted load? 
- Where to store options, say for signing key for JWT, options for generating JWT? in Azure? In `appsettings.json`?
- When it's better to start working on front-end - after back-part is completed? Or right before implementing concurrency mechanism for bookings?
- No unit tests? Only automation test mentioned in `assignment.md`?
- Any architectural decisions regaring front-end (like discussing whether we need state managament like Redux, whether we need some routing for front-end, etc) - can be deferred to the moment when we actually will start  working on front-end?