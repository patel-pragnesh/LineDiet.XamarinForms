﻿using LineDietXF.Helpers;
using LineDietXF.Interfaces;
using LineDietXF.Types;
using LineDietXF.Views;
using Prism;
using Prism.Commands;
using Prism.Navigation;
using Prism.Services;
using System;
using System.Threading.Tasks;
using Xamarin.Forms;

namespace LineDietXF.ViewModels
{
    /// <summary>
    /// The primary tab the user sees on startup. Contains daily messaging based on their current data, and allows
    /// user to set a goal or enter a weight
    /// </summary>
    public class DailyInfoPageViewModel : BaseViewModel, IActiveAware
    {
        // Bindable Properties
        string _todaysWeight = Constants.Strings.DailyInfoPage_UnknownWeight;
        public string TodaysWeight
        {
            get { return _todaysWeight; }
            set { SetProperty(ref _todaysWeight, value); }
        }

        string _todaysMessage = Constants.Strings.DailyInfoPage_Loading;
        public string TodaysMessage
        {
            get { return _todaysMessage; }
            set { SetProperty(ref _todaysMessage, value); }
        }

        string _howToEatText = string.Empty;
        public string HowToEatText
        {
            get { return _howToEatText; }
            set { SetProperty(ref _howToEatText, value); }
        }

        bool _isEnterWeightButtonVisible;
        public bool IsEnterWeightButtonVisible
        {
            get { return _isEnterWeightButtonVisible; }
            set { SetProperty(ref _isEnterWeightButtonVisible, value); }
        }

        bool _isSetGoalButtonVisible;

        public bool IsSetGoalButtonVisible
        {
            get { return _isSetGoalButtonVisible; }
            set { SetProperty(ref _isSetGoalButtonVisible, value); }
        }

        #region IActiveAware implementation

        public event EventHandler IsActiveChanged;

        private bool _isActive;
        public bool IsActive
        {
            get { return _isActive; }
            set
            {
                _isActive = value;
                if (IsActiveChanged != null)
                    IsActiveChanged(this, EventArgs.Empty);

                if (_isActive)
                    Setup();
                else
                    TearDown();
            }
        }

        #endregion

        private bool _isFirstStartup = true;

        // Services
        IDataService DataService { get; set; }
        IWindowColorService WindowColorService { get; set; }
        ISettingsService SettingsService { get; set; }

        // Bindable Commands
        public DelegateCommand AddEntryCommand { get; set; }
        public DelegateCommand SetGoalCommand { get; set; }

        public DailyInfoPageViewModel(INavigationService navigationService, IAnalyticsService analyticsService, IPageDialogService dialogService,
            IDataService dataService, IWindowColorService windowColorService, ISettingsService settingsService) :
            base(navigationService, analyticsService, dialogService)
        {
            // Store off injected services
            DataService = dataService;
            WindowColorService = windowColorService;
            SettingsService = settingsService;

            AddEntryCommand = new DelegateCommand(ShowAddWeightScreen);
            SetGoalCommand = new DelegateCommand(ShowSetGoalScreen);
        }

        void Setup()
        {
            AnalyticsService.TrackPageView(Constants.Analytics.Page_DailyInfo);

            // wire up events
            DataService.UserDataUpdated += DataService_UserDataUpdated;

            // The first run of the app the data service may not have finished loading yet. In that scenario it will fire UserDataUpdated() once done init'ing which will cause the RefreshFromuserDataAsync() to be called.
            // On subsequent navigations it will have already been init'd and should refresh data when returning to this page
            if (DataService.HasBeenInitialized)
                RefreshFromUserDataAsync();
        }

        void TearDown()
        {
            // unwire from events
            if (DataService != null)
                DataService.UserDataUpdated -= DataService_UserDataUpdated;
        }

        void DataService_UserDataUpdated(object sender, EventArgs e)
        {
            RefreshFromUserDataAsync();
        }

        async void ShowAddWeightScreen()
        {
            AnalyticsService.TrackEvent(Constants.Analytics.DailyInfoCategory, Constants.Analytics.DailyInfo_LaunchedAddWeight, 1);

            await NavigationService.NavigateAsync($"{nameof(NavigationPage)}/{nameof(WeightEntryPage)}", useModalNavigation: true);
        }

        async void ShowSetGoalScreen()
        {
            AnalyticsService.TrackEvent(Constants.Analytics.DailyInfoCategory, Constants.Analytics.DailyInfo_LaunchedSetGoal, 1);

            await NavigationService.NavigateAsync($"{nameof(NavigationPage)}/{nameof(SetGoalPage)}", useModalNavigation: true);
        }

        async void RefreshFromUserDataAsync()
        {
            if (_isFirstStartup)
            {
                _isFirstStartup = false;

                // if we need to show the welcome flow don't bother updating data (will be updated when this page appears again)
                if (await ShowWelcomeFlowIfNeeded())
                    return;
            }

            try
            {
                IncrementPendingRequestCount(); // show loading

                // do async data read                                
                var todaysWeightEntry = await DataService.GetWeightEntryForDate(DateTime.Today.Date);
                var goal = await DataService.GetGoal();

                // get all the info needed to update the UI (NOTE:: both goal and todaysWeightEntry could be null)
                var infoForToday = WeightLogicHelpers.GetTodaysDisplayInfo(goal, todaysWeightEntry);

                TodaysWeight = infoForToday.TodaysDisplayWeight;
                TodaysMessage = infoForToday.TodaysMessage;
                HowToEatText = infoForToday.HowToEatMessage;

                IsEnterWeightButtonVisible = infoForToday.IsEnterWeightButtonVisible;
                IsSetGoalButtonVisible = infoForToday.IsSetGoalButtonVisible;

                WindowColorService.ChangeAppBaseColor(infoForToday.ColorToShow);
            }
            catch (Exception ex)
            {
                AnalyticsService.TrackFatalError($"{nameof(RefreshFromUserDataAsync)} - an exception occurred.", ex);
                // NOTE:: not showing an error here as this is not in response to user action. potentially should show a non-intrusive error banner
            }
            finally
            {
                DecrementPendingRequestCount(); // hide loading
            }
        }

        async Task<bool> ShowWelcomeFlowIfNeeded()
        {
            // check if they have seen the getting started flow yet or don't have a goal
            WeightLossGoal existingGoal = null;
            try
            {
                IncrementPendingRequestCount();

                existingGoal = await DataService.GetGoal();
            }
            catch (Exception ex)
            {
                AnalyticsService.TrackFatalError($"{nameof(ShowWelcomeFlowIfNeeded)} - an exception occurred.", ex);
                // NOTE:: not showing an error here as this is not in response to user action. potentially should show a non-intrusive error banner
                return false;
            }
            finally
            {
                DecrementPendingRequestCount(); // hide loading
            }

            bool showGettingStartedFlow = (existingGoal == null && !SettingsService.HasDismissedStartupView);

#if DEBUG
            if (Constants.App.DEBUG_FORCE_SHOW_GETTING_STARTED)
                showGettingStartedFlow = true;
#endif

            if (showGettingStartedFlow)
            {
                AnalyticsService.TrackEvent(Constants.Analytics.DailyInfoCategory, Constants.Analytics.DailyInfo_LaunchingGettingStarted, 1);

                SettingsService.HasDismissedStartupView = true; // don't put them in this flow again

                await Task.Delay(500); // slight delay before fly-up modal so they see the context of the app before it appears
                await NavigationService.NavigateAsync($"{nameof(NavigationPage)}/{nameof(GettingStartedPage)}", useModalNavigation: true);

                return true;
            }

            return false;
        }
    }
}