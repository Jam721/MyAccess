using Microsoft.Extensions.Logging;
using MyAccess.Services;

namespace MyAccess;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
			});

		builder.Services.AddMauiBlazorWebView();
		
		builder.Services.AddSingleton<AuthService>();
		builder.Services.AddSingleton(provider => {
			var authService = provider.GetRequiredService<AuthService>();
			return new DatabaseService("Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=123", authService);
		});
		builder.Services.AddScoped<PdfReportService>();


#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
