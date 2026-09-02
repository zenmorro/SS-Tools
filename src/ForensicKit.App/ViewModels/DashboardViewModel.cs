using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForensicKit.App.Services;
using ForensicKit.Core.Models;
using ForensicKit.Core.Services;

namespace ForensicKit.App.ViewModels;

public sealed partial class DashboardViewModel : ObservableObject
{
    public const string AllCategory = "Tutti";
    public const string FavoritesCategory = "Preferiti";

    private readonly IManifestService _manifest;
    private readonly IToolInstallService _install;
    private readonly IExecutionService _execution;
    private readonly ISignatureService _signature;
    private readonly IAuditLogService _audit;
    private readonly IDialogService _dialog;
    private readonly ISettingsService _settings;

    private readonly List<ToolCardViewModel> _allTools = new();

    public DashboardViewModel(
        IManifestService manifest,
        IToolInstallService install,
        IExecutionService execution,
        ISignatureService signature,
        IAuditLogService audit,
        IDialogService dialog,
        ISettingsService settings)
    {
        _manifest = manifest;
        _install = install;
        _execution = execution;
        _signature = signature;
        _audit = audit;
        _dialog = dialog;
        _settings = settings;

        ToolsView = CollectionViewSource.GetDefaultView(VisibleTools);
    }

    public ObservableCollection<string> Categories { get; } = new();
    public ObservableCollection<ToolCardViewModel> VisibleTools { get; } = new();
    public ICollectionView ToolsView { get; }

    [ObservableProperty] private string _selectedCategory = "";
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _catalogSource = "";
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _isLoading;

    public async Task InitializeAsync()
    {
        IsLoading = true;
        StatusMessage = "Caricamento catalogo…";
        try
        {
            var load = await _manifest.LoadAsync();
            CatalogSource = load.Source switch
            {
                "remote" => "Catalogo: remoto (aggiornato)",
                "local" => "Catalogo: copia locale",
                _ => "Catalogo: incluso nell'app"
            };
            if (!string.IsNullOrWhiteSpace(load.Warning))
                StatusMessage = load.Warning;

            _allTools.Clear();
            foreach (var tool in load.Manifest.Tools)
            {
                _allTools.Add(new ToolCardViewModel(
                    tool, _install, _execution, _signature, _audit, _dialog, _settings));
            }

            BuildCategories();
            SelectedCategory = Categories.FirstOrDefault() ?? "";
            ApplyFilter();

            // Background update check (non-blocking, best-effort).
            _ = CheckUpdatesAsync();
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void BuildCategories()
    {
        Categories.Clear();
        Categories.Add(AllCategory);
        Categories.Add(FavoritesCategory);
        foreach (var cat in _allTools
                     .Select(t => t.Category)
                     .Distinct()
                     .OrderBy(c => c, StringComparer.CurrentCultureIgnoreCase))
        {
            Categories.Add(cat);
        }
    }

    private async Task CheckUpdatesAsync()
    {
        foreach (var tool in _allTools)
        {
            try { await tool.CheckUpdateAsync(); } catch { /* best effort */ }
        }
    }

    partial void OnSelectedCategoryChanged(string value) => ApplyFilter();
    partial void OnSearchTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        VisibleTools.Clear();

        IEnumerable<ToolCardViewModel> query = _allTools;

        if (SelectedCategory == FavoritesCategory)
            query = query.Where(t => _settings.Current.Favorites.Contains(t.Tool.Id));
        else if (!string.IsNullOrWhiteSpace(SelectedCategory) && SelectedCategory != AllCategory)
            query = query.Where(t => t.Category == SelectedCategory);

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var term = SearchText.Trim();
            query = query.Where(t =>
                t.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                t.Description.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                t.Author.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var tool in query.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase))
            VisibleTools.Add(tool);

        StatusMessage = $"{VisibleTools.Count} tool";
    }

    [RelayCommand]
    private async Task RefreshAsync() => await InitializeAsync();
}
