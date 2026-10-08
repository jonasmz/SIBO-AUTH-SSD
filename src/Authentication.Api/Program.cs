using Authentication.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddAuthenticationInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();

app.Run();

public partial class Program
{
}
