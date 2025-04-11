using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.OpenApi.Models; // Для OpenApiInfo и других моделей Swagger
using Swashbuckle.AspNetCore.SwaggerGen; // Для AddSwaggerGen
using Swashbuckle.AspNetCore.SwaggerUI; // Для UseSwaggerUI
using System.Reflection;

var builder = WebApplication.CreateBuilder(args);

// Увеличиваем лимит размера тела запроса для IIS
builder.Services.Configure<IISServerOptions>(options =>
{
    options.MaxRequestBodySize = 100_000_000; // 100 МБ (или больше, если нужно)
});

// Увеличиваем лимит размера тела запроса для Kestrel
builder.Services.Configure<KestrelServerOptions>(options =>
{
    options.Limits.MaxRequestBodySize = 100_000_000; // 100 МБ
});

// Увеличиваем лимит для multipart/form-data
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 100_000_000; // 100 МБ
});

// Добавляем поддержку контроллеров
builder.Services.AddControllers();

// Регистрируем Swagger
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "MelanCloud API",
        Version = "v1",
        Description = "API для управления файлами в облачном хранилище MelanCloud",
        Contact = new OpenApiContact
        {
            Name = "MelanCloud Support",
            Email = "csgomanagement@gmail.com",
            Url = new Uri("https://mircord.online/web")
        }
    });

    // Включаем XML-комментарии (если они есть)
    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        c.IncludeXmlComments(xmlPath);
    }
});

var app = builder.Build();

// Включаем middleware для Swagger
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "MelanCloud API v1");
    c.RoutePrefix = string.Empty; // Устанавливаем Swagger UI по корневому пути (например, http://localhost:5000/)
});

app.UseRouting();
app.UseAuthorization();
app.MapControllers();

app.Run();