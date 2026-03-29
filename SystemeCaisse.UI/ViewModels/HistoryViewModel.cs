using Microsoft.EntityFrameworkCore;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using SystemeCaisse.Core.Entities;
using SystemeCaisse.Infrastructure.Data;
using System.Linq;
using System;
using System.Collections.Generic;

using SystemeCaisse.UI.Services;

namespace SystemeCaisse.UI.ViewModels
{
    public class HistoryViewModel : INotifyPropertyChanged
    {
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private AppDbContext _context;

        public ObservableCollection<Vente> Sales { get; private set; }

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
            set { _searchText = value; OnPropertyChanged(); _ = LoadDataAsync(); }
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

        public HistoryViewModel(IDbContextFactory<AppDbContext> contextFactory, PrintService printService)
        {
            _contextFactory = contextFactory;
            _printService = printService;
            
            LoadCommand = new BasicRelayCommand(_ => _ = LoadDataAsync());
            ReprintCommand = new BasicRelayCommand(_ => 
            {
                if (SelectedSale != null)
                {
                    // Ensure Enterprise is loaded
                    var entreprise = _context.Entreprise.FirstOrDefault() ?? new Entreprise { Nom = "Inconnu" };
                    _printService.PrintTicket(SelectedSale, entreprise);
                }
            }, _ => SelectedSale != null);

            ViewTicketCommand = new BasicRelayCommand(_ => 
            {
                if (SelectedSale != null)
                {
                    var entreprise = _context.Entreprise.FirstOrDefault() ?? new Entreprise { Nom = "Inconnu" };
                    var win = new Views.ReceiptSummaryWindow(SelectedSale, entreprise, SelectedSale.MonnaieRendue, true);
                    win.Owner = Application.Current.MainWindow;
                    win.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                    win.ShowDialog();
                }
            }, _ => SelectedSale != null);

            ClearSearchCommand = new BasicRelayCommand(_ => SearchText = string.Empty);
            SetPeriodCommand = new BasicRelayCommand(p => { if (p is string s) SetPeriod(s); });
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

        public async Task LoadDataAsync()
        {
            await Task.Run(async () => 
            {
            _context?.Dispose();
            _context = _contextFactory.CreateDbContext();

            var query = _context.Ventes
                .Include(v => v.Lignes)
                .Where(v => v.CreatedAt.Date >= StartDate.Date && v.CreatedAt.Date <= EndDate.Date);

            if (PaymentFilter != "Tous")
            {
               string filterKey = PaymentFilter.ToLower();
               if (filterKey == "espèces" || filterKey == "especes") 
                   query = query.Where(v => v.MoyenPaiement.ToLower() == "especes");
               else if (filterKey == "cb")
                   query = query.Where(v => v.MoyenPaiement.ToLower() == "cb");
               else if (filterKey == "mixte")
                   query = query.Where(v => v.MoyenPaiement.ToLower() == "mixte");
            }

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                query = query.Where(v => v.NumeroTicket.Contains(SearchText));
            }

            var salesList = query.OrderByDescending(v => v.CreatedAt).ToList();
            
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => 
            {
                Sales = new ObservableCollection<Vente>(salesList);
                TotalPeriod = salesList.Sum(v => v.Total);
                CountPeriod = salesList.Count;
                TotalCashPeriod = salesList.Sum(v => v.MontantEspeces);
                TotalCardPeriod = salesList.Sum(v => v.MontantCB);
                OnPropertyChanged(nameof(Sales));
            });
        });
    }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) 
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
