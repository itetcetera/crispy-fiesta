using Microsoft.Extensions.FileProviders;
using Mule.Server.Hosting;
using Mule.Server.Hubs;
using Mule.Server.Identity;
using Mule.Server.Rooms;
using Mule.Server.Wire;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<RoomRegistry>();
builder.Services.AddSingleton<IIdentityService, GuestIdentityService>();
builder.Services.Configure<TickOptions>(builder.Configuration.GetSection("Ticks"));
builder.Services.AddHostedService<RoomTicker>();
builder.Services.AddSignalR().AddJsonProtocol(o => EventWire.Configure(o.PayloadSerializerOptions));
// The client's dev server runs on another port.
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .SetIsOriginAllowed(origin => new Uri(origin).Host is "localhost" or "127.0.0.1")
    .AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

var app = builder.Build();
app.UseCors();
// The built browser client: client/dist next to the server folder, or the path in "ClientPath";
// without it, wwwroot's placeholder page.
var clientPath = builder.Configuration["ClientPath"]
    ?? Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "..", "client", "dist"));
var files = Directory.Exists(clientPath) ? new PhysicalFileProvider(clientPath) : builder.Environment.WebRootFileProvider;
app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
app.UseStaticFiles(new StaticFileOptions { FileProvider = files });
app.MapHub<GameHub>("/hub");
app.MapGet("/health", () => "ok");
// Invite links (/r/ABC123) open the client, which reads the code from the address.
app.MapFallbackToFile("index.html", new StaticFileOptions { FileProvider = files });
app.Run();

public partial class Program;
