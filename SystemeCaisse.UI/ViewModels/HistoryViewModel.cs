using Microsoft.EntityFrameworkCore;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using SystemeCaisse.Core.Entities;
using SystemeCaisse.Infrastructure.Configuration;
using System.Linq;
using System;
using System.Collections.Generic;

using SystemeCaisse.UI.Services;

namespace SystemeCaisse.UI.ViewModels
{
    public class HistoryViewModel : INotifyPropertyChanged
    {
        private readonly CaisseServices _services;
        private int _loadVersion;

        /// <summary>Date du dernier chargement réussi.</summary>
        public DateTime LastLoaded { get; private set; } = DateTime.MinValue;

        public ObservableCollection<Vente> Sales { get; private set; } = new();

        // Filters
        private DateTime _startDate = DateTime.Today;
        public DateTime StartDate
        {
            get => _startDate;
            set { _startDate = value; OnPropertyChanged(); _ = LoadDataAsync(); }
        }

        private DateTime _endDate = DateTime.Today;
        public DateTime EndDate
        {
            get => _endDate;
            set { _endDate = value; OnPropertyChanged(); _ = LoadDataAsync(); }
        }

        private string _paymentFilter = "Tous";
        public string PaymentFilter
        {
            get => _paymentFilter;
            set { _paymentFilter = value; OnPropertyChanged(); _ = LoadDataAsync(); }
        }

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set { _searchText = value; OnPropertyChanged(); _ = LoadDataAsync(debounce: true); }
        }

        private Vente _selectedSale;
        public Vente SelectedSale
        {
            get => _selectedSale;
            set { _selectedSale = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsDetailVisible)); }
        }
        
        public bool IsDetailVisible => SelectedSale != null;

        // Stats
        private decimal _totalPeriod;
        public decimal TotalPeriod
        {
            get => _totalPeriod;
            set { _totalPeriod = value; OnPropertyChanged(); }
        }

        private int _countPeriod;
        public int CountPeriod
        {
            get => _countPeriod;
            set { _countPeriod = value; OnPropertyChanged(); }
        }

        private decimal _totalCashPeriod;
        public decimal TotalCashPeriod
        {
            get => _totalCashPeriod;
            set { _totalCashPeriod = value; OnPropertyChanged(); }
        }

        private decimal _totalCardPeriod;
        public decimal TotalCardPeriod
        {
            get => _totalCardPeriod;
            set { _totalCardPeriod = value; OnPropertyChanged(); }
        }

        public ICommand LoadCommand { get; }
        public ICommand ReprintCommand { get; }
        public ICommand ViewTicketCommand { get; }
        public ICommand ClearSearchCommand { get; }
        public ICommand SetPeriodCommand { get; }
        private readonly PrintService _printService;

        public HistoryViewModel(CaisseServices services, PrintService printService)
        {
            _services = services;
            _printService = printService;
            
            LoadCommand = new BasicRelayCommand(_ => _ = LoadDataAsync());
            ReprintCommand = new BasicRelayCommand(async _ => 
            {
                if (SelectedSale != null)
                {
                    var entreprise = await GetEntrepriseAsync();
                    _printService.PrintTicket(SelectedSale, entreprise);
                }
            }, _ => SelectedSale != null);

            ViewTicketCommand = new BasicRelayCommand(async _ => 
            {
                if (SelectedSale != null)
                {
                    var entreprise = await GetEntrepriseAsync();
                    var win = new Views.ReceiptSummaryWindow(SelectedSale, entreprise, SelectedSale.MonnaieRendue, true);
                    win.Owner = Services.WindowHelper.GetAdminWindow();
                    win.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                    win.ShowDialog();
                }
            }, _ => SelectedSale != null);

            ClearSearchCommand = new BasicRelayCommand(_ => SearchText = string.Empty);
            SetPeriodCommand = new BasicRelayCommand(p => { if (p is string s) SetPeriod(s); });
        }

        private async Task<Entreprise> GetEntrepriseAsync()
        {
            try
            {
                return await _services.Catalog.GetEntrepriseAsync() ?? new Entreprise { Nom = "Inconnu" };
            }
            catch (Exception ex)
            {
                AppLog.Warn("Historique", $"Lecture de l'entreprise impossible, utilisation du cache : {ex.Message}");
                return _services.Cache.Load()?.Entreprise ?? new Entreprise { Nom = "Inconnu" };
            }
        }

        public async Task InitializeAsync()
        {
            await LoadDataAsync();
        }


        private void SetPeriod(string period)
        {
            var today = DateTime.Today;
            switch (period)
            {
                case "Today":
                    StartDate = today;
                    EndDate = today;
                    break;
                case "Yesterday":
                    StartDate = today.AddDays(-1);
                    EndDate = today.AddDays(-1);
                    break;
                case "Week":
                    StartDate = today.AddDays(-6);
                    EndDate = today;
                    break;
                case "Month":
                    StartDate = new DateTime(today.Year, today.Month, 1);
                    EndDate = today;
                    break;
                case "Year":
                    StartDate = new DateTime(today.Year, 1, 1);
                    EndDate = today;
                    break;
            }
            _ = LoadDataAsync();
        }

        /// <summary>
        /// Recharge les ventes de la période. Plusieurs appels rapprochés (frappe, changement de dates)
        /// sont fusionnés : seul le résultat de la DERNIÈRE demande est affiché.
        /// </summary>
        public async Task LoadDataAsync(bool debounce = false)
        {
            int version = Interlocked.Increment(ref _loadVersion);
            if (debounce) await Task.Delay(300);
            if (version != _loadVersion) return;

            var start = StartDate.Date;
            var endExclusive = EndDate.Date.AddDays(1);
            var paymentFilter = PaymentFilter;
            var search = SearchText?.Trim();

            try
            {
                await using var db = await _services.Factory.CreateDbContextAsync();

                // Plage [début ; fin+1[ : exploite l'index sur CreatedAt (CreatedAt.Date l'empêchait).
                var query = db.Ventes.AsNoTracking()
                    .Include(v => v.Lignes)
                    .Where(v => v.CreatedAt >= start && v.CreatedAt < endExclusive);

                if (paymentFilter != "Tous")
                {
                   string filterKey = paymentFilter.ToLower();
                   if (filterKey == "espèces" || filterKey == "especes") 
                       query = query.Where(v => v.MoyenPaiement.ToLower() == "especes");
                   else if (filterKey == "cb")
                       query = query.Where(v => v.MoyenPaiement.ToLower() == "cb");
                   else if (filterKey == "mixte")
                       query = query.Where(v => v.MoyenPaiement.ToLower() == "mixte");
                }

                if (!string.IsNullOrWhiteSpace(search))
                {
                    query = query.Where(v => v.NumeroTicket.Contains(search));
                }

                var salesList = await query.OrderByDescending(v => v.CreatedAt).AsSplitQuery().ToListAsync();
                if (version != _loadVersion) return; // une demande plus récente est en cours

                int? selectedId = SelectedSale?.Id;
                Sales = new ObservableCollection<Vente>(salesList);
                TotalPeriod = salesList.Sum(v => v.Total);
                CountPeriod = salesList.Count;
                TotalCashPeriod = salesList.Sum(v => v.MontantEspeces);
                TotalCardPeriod = salesList.Sum(v => v.MontantCB);
                OnPropertyChanged(nameof(Sales));
                if (selectedId.HasValue) SelectedSale = Sales.FirstOrDefault(v => v.Id == selectedId.Value)!;
                LastLoaded = DateTime.Now;
            }
            catch (Exception ex)
            {
                AppLog.Error("Historique", "Chargement des ventes impossible", ex);
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) 
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
