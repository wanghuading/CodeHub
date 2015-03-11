using System;
using CodeHub.Core.Data;
using CodeHub.Core.Services;
using System.Threading.Tasks;
using ReactiveUI;
using System.Reactive.Linq;
using CodeHub.Core.Messages;
using System.Reactive;
using CodeHub.Core.Factories;
using CodeHub.Core.Utilities;

namespace CodeHub.Core.ViewModels.Accounts
{
    public class LoginViewModel : BaseViewModel
    {
        public const string ClientId = "72f4fb74bdba774b759d";
        public const string ClientSecret = "9253ab615f8c00738fff5d1c665ca81e581875cb";
        public static readonly string RedirectUri = "http://dillonbuchanan.com/";
        private const string DefaultWebDomain = "https://github.com";
        private readonly ILoginService _loginFactory;
        private readonly IAccountsRepository _accountsRepository;
        private readonly IAlertDialogFactory _alertDialogService;

        private readonly ObservableAsPropertyHelper<string> _loginUrl;
        public string LoginUrl
        {
            get { return _loginUrl.Value; }
        }

        public bool IsEnterprise { get; set; }

        private GitHubAccount _attemptedAccount;
        public GitHubAccount AttemptedAccount
        {
            get { return _attemptedAccount; }
            set { this.RaiseAndSetIfChanged(ref _attemptedAccount, value); }
        }

        private string _webDomain;
        public string WebDomain
        {
            get { return _webDomain; }
            set { this.RaiseAndSetIfChanged(ref _webDomain, value); }
        }

        public IReactiveCommand<GitHubAccount> LoginCommand { get; private set; }

        public IReactiveCommand<Unit> ShowLoginOptionsCommand { get; private set; }

        public IReactiveCommand<Unit> LoadCommand { get; private set; }

        private string _code;
        public string Code
        {
            get { return _code; }
            set { this.RaiseAndSetIfChanged(ref _code, value); }
        }

        public LoginViewModel(ILoginService loginFactory, 
                              IAccountsRepository accountsRepository,
                              IActionMenuFactory actionMenuService,
                              IAlertDialogFactory alertDialogService)
        {
            _loginFactory = loginFactory;
            _accountsRepository = accountsRepository;
            _alertDialogService = alertDialogService;

            Title = "Login";

            var basicLogin = ReactiveCommand.Create().WithSubscription(_ =>
                NavigateTo(this.CreateViewModel<AddWebAccountViewModel>()));

            var oauthLogin = ReactiveCommand.Create().WithSubscription(_ =>
                NavigateTo(this.CreateViewModel<OAuthLoginViewModel>()));

            var canLogin = this.WhenAnyValue(x => x.Code).Select(x => !string.IsNullOrEmpty(x));
            var loginCommand = ReactiveCommand.CreateAsyncTask(canLogin,_ => Login(Code));
            loginCommand.Subscribe(x => MessageBus.Current.SendMessage(new LogoutMessage()));
            LoginCommand = loginCommand;

            ShowLoginOptionsCommand = ReactiveCommand.CreateAsyncTask(t =>
            {
                var actionMenu = actionMenuService.Create(Title);
                actionMenu.AddButton("Login via BASIC", basicLogin);
                actionMenu.AddButton("Login via Token", oauthLogin);
                return actionMenu.Show();
            });

            _loginUrl = this.WhenAnyValue(x => x.WebDomain)
                .IsNotNull()
                .Select(x => x.TrimEnd('/'))
                .Select(x => 
                    string.Format(x + "/login/oauth/authorize?client_id={0}&redirect_uri={1}&scope={2}", 
                    ClientId, Uri.EscapeDataString(RedirectUri), Uri.EscapeDataString(string.Join(",", OctokitClientFactory.Scopes))))
                .ToProperty(this, x => x.LoginUrl);

            LoadCommand = ReactiveCommand.CreateAsyncTask(async t =>
            {
                if (IsEnterprise && string.IsNullOrEmpty(WebDomain))
                {
                    var response = await alertDialogService.PromptTextBox("Enterprise URL",
                        "Please enter the webpage address for the GitHub Enterprise installation",
                        DefaultWebDomain, "Ok");
                    WebDomain = response;
                }
                else
                {
                    WebDomain = DefaultWebDomain;
                }
            });

            LoadCommand.ThrownExceptions.Take(1).Subscribe(x => Dismiss());
        }

        private async Task<GitHubAccount> Login(string code)
        {
            string apiUrl;
            if (IsEnterprise)
            {
                apiUrl = WebDomain;
                if (!apiUrl.StartsWith("http://") && !apiUrl.StartsWith("https://"))
                    apiUrl = "https://" + apiUrl;
                if (!apiUrl.EndsWith("/"))
                    apiUrl += "/";
                if (!apiUrl.Contains("/api/"))
                    apiUrl += "api/v3/";

                apiUrl = apiUrl.TrimEnd('/');
            }
            else
            {
                apiUrl = GitHubSharp.Client.DefaultApi;
            }

            using (_alertDialogService.Activate("Logging in..."))
            {
                var account = AttemptedAccount;
                var loginData = await _loginFactory.LoginWithToken(ClientId, ClientSecret, code, RedirectUri, WebDomain, apiUrl, account);
                await _accountsRepository.SetDefault(loginData.Account);
                return loginData.Account;
            }
        }
    }
}

