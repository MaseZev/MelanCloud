using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.OpenApi.Models; // ��� OpenApiInfo � ������ ������� Swagger
using Swashbuckle.AspNetCore.SwaggerGen; // ��� AddSwaggerGen
using Swashbuckle.AspNetCore.SwaggerUI; // ��� UseSwaggerUI
using System.Reflection;

var builder = WebApplication.CreateBuilder(args);

// ����������� ����� ������� ���� ������� ��� IIS
builder.Services.Configure<IISServerOptions>(options =>
{
    options.MaxRequestBodySize = 100_000_000; // 100 �� (��� ������, ���� �����)
});

// ����������� ����� ������� ���� ������� ��� Kestrel
builder.Services.Configure<KestrelServerOptions>(options =>
{
    options.Limits.MaxRequestBodySize = 100_000_000; // 100 ��
});

// ����������� ����� ��� multipart/form-data
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 100_000_000; // 100 ��
});

// ��������� ��������� ������������
builder.Services.AddControllers();

// ������������ Swagger
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "MelanCloud API",
        Version = "v1",
        Description = "API ��� ���������� ������� � �������� ��������� MelanCloud",
        Contact = new OpenApiContact
        {
            Name = "MelanCloud Support",
            Email = "csgomanagement@gmail.com",
            Url = new Uri("https://mircord.online/web")
        }
    });

    // �������� XML-����������� (���� ��� ����)
    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        c.IncludeXmlComments(xmlPath);
    }
});

var app = builder.Build();

// �������� middleware ��� Swagger
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "MelanCloud API v1");
    c.RoutePrefix = string.Empty; // ������������� Swagger UI �� ��������� ���� (��������, http://localhost:5000/)
});

app.UseRouting();
app.UseAuthorization();
app.MapControllers();

app.Run();