using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Microsoft.PowerToys.Settings.UI.Library;
using Wox.Plugin;
using Wox.Plugin.Logger;
using BrowserSearch.Browsers;

namespace Community.Powertoys.Run.Plugin.BrowserSearch
{
    public class Main : IPlugin, ISettingProvider, IReloadable
    {
        public static string PluginID => "E5A9FC7A3F7F4320BE612DA95C57C32D";
        public string Name => "Browser Search";
        public string Description => "Searches in your browser's history.";

        public IEnumerable<PluginAdditionalOption> AdditionalOptions => new List<PluginAdditionalOption>()
        {
            new()
            {
                Key = MaxResults,
                DisplayLabel = "Maximum number of results",
                DisplayDescription = "Maximum number of results to show. Set to -1 to show all (may decrease performance)",
                PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Numberbox,
                NumberValue = 15
            },
            new()
            {
                Key = SingleProfile,
                DisplayLabel = "Browser profile",
                DisplayDescription = "The name of the browser profile whose history will be loaded.\n" +
                                     "If empty, the history of ALL profiles will be loaded.",
                PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Textbox
            }
        };

        private const string MaxResults = nameof(MaxResults);
        private const string SingleProfile = nameof(SingleProfile);
        private PluginInitContext? _context;
        private IBrowser? _defaultBrowser;
        private long _lastUpdateTickCount = -300L;
        private int _maxResults;
        private string? _selectedProfileName;

        public void Init(PluginInitContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            InitDefaultBrowser();
        }

        public void ReloadData()
        {
            if (_context is null)
            {
                return;
            }

            // When the plugin is disabled and then re-enabled,
            // ReloadData() is called multiple times so this is needed
            if (Environment.TickCount64 - _lastUpdateTickCount >= 300)
            {
                InitDefaultBrowser();
                _lastUpdateTickCount = Environment.TickCount64;
            }
        }

        public Control CreateSettingPanel()
        {
            throw new NotImplementedException();
        }

        public void UpdateSettings(PowerLauncherPluginSettings settings)
        {
            _maxResults = (int)(settings?.AdditionalOptions?.FirstOrDefault(x => x.Key == MaxResults)?.NumberValue ?? 15);

            PluginAdditionalOption? profile = settings?.AdditionalOptions?.FirstOrDefault(x => x.Key == SingleProfile);
            if (profile is not null && profile.TextValue?.Length > 0)
            {
                _selectedProfileName = profile.TextValue;
            }
            else
            {
                _selectedProfileName = null;
            }
        }

        private void InitDefaultBrowser()
        {
            _defaultBrowser = null;
            string localappdata = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string roamingappdata = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            _defaultBrowser = new Chromium(
                Path.Join(localappdata, @"Packages\TheBrowserCompany.Arc_ttt1ap7aakyb4\LocalCache\Local\Arc\User Data"), _selectedProfileName);

            Log.Info($"Initializing browser 'Arc'", typeof(Main));
            _defaultBrowser.Init();
        }

        public List<Result> Query(Query query)
        {
            ArgumentNullException.ThrowIfNull(query);
            if (_defaultBrowser is null)
            {
                return [];
            }

            List<Result> history = _defaultBrowser.GetHistory();
            // Happens when the user only types our ActionKeyword ("b?" by default)
            if (string.IsNullOrEmpty(query.Search))
            {
                // Returning the whole history here makes the search lag, so return only some entries
                int amount = _maxResults == -1 ? 15 : _maxResults;
                return history.TakeLast(amount).ToList();
            }

            List<Result> results = new(history.Count);
            for (int i = 0; i < history.Count; i++)
            {
                Result r = history[i];

                int score = CalculateScore(query.Search, r.Title, r.SubTitle);
                if (score <= 0)
                {
                    continue;
                }

                r.Score = score;
                results.Add(r);
            }

            if (_maxResults != -1)
            {
                // Rendering the UI of every search entry is slow, so only show top results
                results.Sort((x, y) => y.Score.CompareTo(x.Score));
                results = results.Take(_maxResults).ToList();
            }

            return results;
        }

        private int CalculateScore(string query, string title, string url)
        {
            // Since PT Run's FuzzySearch is too slow, and the history usually has a lot of entries,
            // lets calculate the scores manually using a faster (but less accurate) method
            float titleScore = title.Contains(query, StringComparison.InvariantCultureIgnoreCase)
                ? ((float)query.Length / (float)title.Length * 100f)
                : 0;
            float urlScore = url.Contains(query, StringComparison.InvariantCultureIgnoreCase)
                ? ((float)query.Length / (float)url.Length * 100f)
                : 0;

            float score = new[] { titleScore, urlScore }.Max();
            score += _defaultBrowser!.CalculateExtraScore(query, title, url);

            return (int)score;
        }
    }
}
