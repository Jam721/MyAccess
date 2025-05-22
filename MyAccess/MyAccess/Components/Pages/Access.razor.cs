using Microsoft.AspNetCore.Components;
using MyAccess.Services;
using Microsoft.JSInterop;
using Npgsql;

namespace MyAccess.Pages
{
    public class Home : ComponentBase
    {
        protected List<DatabaseInfo>? Databases;
        protected bool showDialog = false;
        protected string newDbName = string.Empty;
        protected string errorMessage = string.Empty;

        [Inject]
        public DatabaseService DbService { get; set; } = default!;

        [Inject]
        public NavigationManager Navigation { get; set; } = default!;

        [Inject]
        public IJSRuntime JSRuntime { get; set; } = default!;

        protected override async Task OnInitializedAsync()
        {
            await LoadDatabases();
        }

        private async Task LoadDatabases()
        {
            try
            {
                // Сбрасываем список перед обновлением
                Databases = null;
                await InvokeAsync(StateHasChanged);

                var dbNames = await DbService.GetDatabasesAsync();
                var newDatabases = new List<DatabaseInfo>();

                // Параллельная загрузка таблиц
                var loadTasks = dbNames.Select(async name => 
                {
                    var tables = await DbService.GetTablesAsync(name);
                    return new DatabaseInfo
                    {
                        Name = name,
                        Tables = tables
                    };
                });

                foreach (var task in loadTasks)
                {
                    newDatabases.Add(await task);
                }

                Databases = newDatabases.OrderBy(d => d.Name).ToList();
            }
            catch (Exception ex)
            {
                errorMessage = $"Ошибка загрузки: {ex.Message}";
            }
            finally
            {
                await InvokeAsync(StateHasChanged);
            }
        }

        protected void ShowAddDialog()
        {
            showDialog = true;
            newDbName = string.Empty;
            errorMessage = string.Empty;
            StateHasChanged();
        }
        
        protected async Task CreateDatabase()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(newDbName))
                {
                    errorMessage = "Название БД не может быть пустым";
                    return;
                }

                await DbService.CreateDatabaseAsync(newDbName);
                await LoadDatabases();
                showDialog = false;
            }
            catch (Exception ex)
            {
                errorMessage = $"Ошибка создания: {ex.Message}";
            }
            finally
            {
                StateHasChanged();
            }
        }
        
        protected async Task DeleteDatabase(string dbName)
        {
            try
            {
                var confirmed = await JSRuntime.InvokeAsync<bool>("confirm", 
                    $"Вы уверены, что хотите удалить базу данных '{dbName}'?");

                if (!confirmed) return;

                await DbService.DropDatabaseAsync(dbName);
                await LoadDatabases(); // Обновляем список
            }
            catch (PostgresException ex) when (ex.SqlState == "3D000")
            {
                errorMessage = $"База данных '{dbName}' не существует";
                await LoadDatabases(); // Принудительно обновляем список
            }
            catch (Exception ex)
            {
                errorMessage = $"Ошибка: {ex.Message}";
            }
            finally
            {
                StateHasChanged();
            }
        }
        
        protected void CancelDialog()
        {
            showDialog = false;
            StateHasChanged();
        }

        protected void OpenDatabase(string dbName)
        {
            Navigation.NavigateTo($"/database/{dbName}");
        }
    }

    public class DatabaseInfo
    {
        public string Name { get; set; } = string.Empty;
        public List<string> Tables { get; set; } = new();
    }
}