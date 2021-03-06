﻿using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Browser;
using Raven.Abstractions.Data;
using Raven.Client.Document;
using Raven.Studio.Commands;
using Raven.Studio.Infrastructure;

namespace Raven.Studio.Models
{
	public class ServerModel : Model, IDisposable
	{
		public readonly string Url;
		public const string DefaultDatabaseName = "Default";
		private DocumentStore documentStore;
		private DatabaseModel[] defaultDatabase;

		private string buildNumber;
		private bool singleTenant;

		public string BuildNumber
		{
			get { return buildNumber; }
			private set { buildNumber = value; OnPropertyChanged(); }
		}

		public ServerModel()
			: this(DetermineUri())
		{
			RefreshRate = TimeSpan.FromMinutes(2);
		}

		private ServerModel(string url)
		{
			Url = url;
			Databases = new BindableCollection<DatabaseModel>(model => model.Name);
			SelectedDatabase = new Observable<DatabaseModel>();
			License = new Observable<LicensingStatus>();
			Initialize();
		}

		private void Initialize()
		{
			documentStore = new DocumentStore
			{
				Url = Url
			};

			var urlParser = new UrlParser(UrlUtil.Url);
			var apiKey = urlParser.GetQueryParam("api-key");
			if (string.IsNullOrEmpty(apiKey) == false)
				documentStore.ApiKey = apiKey;

			documentStore.Initialize();

			// We explicitly enable this for the Studio, we rely on SL to actually get us the credentials, and that 
			// already gives the user a clear warning about the dangers of sending passwords in the clear. I think that 
			// this is sufficient warning and we don't require an additional step, so we can disable this check safely.
			documentStore.JsonRequestFactory.
				EnableBasicAuthenticationOverUnsecureHttpEvenThoughPasswordsWouldBeSentOverTheWireInClearTextToBeStolenByHackers =
				true;
			
			documentStore.JsonRequestFactory.ConfigureRequest += (o, eventArgs) =>
			{
				if (onWebRequest != null)
					onWebRequest(eventArgs.Request);
			};

			SetCurrentDatabase(new UrlParser(UrlUtil.Url));

			DisplayBuildNumber();
			DisplyaLicenseStatus();

			var changeDatabaseCommand = new ChangeDatabaseCommand();
			SelectedDatabase.PropertyChanged += (sender, args) =>
			{
				if (SelectedDatabase.Value == null)
					return;
				var databaseName = SelectedDatabase.Value.Name;
				if (changeDatabaseCommand.CanExecute(databaseName))
					changeDatabaseCommand.Execute(databaseName);
			};
		}

		public override Task TimerTickedAsync()
		{
			if (singleTenant)
				return null;

			return documentStore.AsyncDatabaseCommands.GetDatabaseNamesAsync()
				.ContinueOnSuccess(names =>
				{
					var databaseModels = names.Select(db => new DatabaseModel(db, documentStore.AsyncDatabaseCommands.ForDatabase(db)));
					Databases.Match(defaultDatabase.Concat(databaseModels).ToArray());
				})
				.Catch();
		}

		public bool SingleTenant
		{
			get { return singleTenant; }
		}

		public Observable<DatabaseModel> SelectedDatabase { get; private set; }
		public BindableCollection<DatabaseModel> Databases { get; private set; }

		public void Dispose()
		{
			documentStore.Dispose();
		}

		private static string DetermineUri()
		{
			if (HtmlPage.Document.DocumentUri.Scheme == "file")
			{
				return "http://localhost:8080";
			}
			var localPath = HtmlPage.Document.DocumentUri.LocalPath;
			var lastIndexOfRaven = localPath.LastIndexOf("/raven/", StringComparison.Ordinal);
			if (lastIndexOfRaven != -1)
			{
				localPath = localPath.Substring(0, lastIndexOfRaven);
			}
			return new UriBuilder(HtmlPage.Document.DocumentUri)
			{
				Path = localPath,
				Fragment = ""
			}.Uri.ToString();
		}

		public void SetCurrentDatabase(UrlParser urlParser)
		{
			var databaseName = urlParser.GetQueryParam("database");
			if (databaseName == null)
			{
				defaultDatabase = new[] {new DatabaseModel(DefaultDatabaseName, documentStore.AsyncDatabaseCommands)};
				Databases.Set(defaultDatabase);
				SelectedDatabase.Value = defaultDatabase[0];
				return;
			}
			if (SelectedDatabase.Value != null && SelectedDatabase.Value.Name == databaseName)
				return;
			var database = Databases.FirstOrDefault(x => x.Name == databaseName);
			if (database != null)
			{
				SelectedDatabase.Value = database;
				return;
			}
			singleTenant = urlParser.GetQueryParam("api-key") != null;
			var databaseCommands = databaseName.Equals("default", StringComparison.OrdinalIgnoreCase)
			                       	? documentStore.AsyncDatabaseCommands.ForDefaultDatabase()
			                       	: documentStore.AsyncDatabaseCommands.ForDatabase(databaseName);
			var databaseModel = new DatabaseModel(databaseName, databaseCommands);
			Databases.Add(databaseModel);
			SelectedDatabase.Value = databaseModel;
		}

		private void DisplayBuildNumber()
		{
			SelectedDatabase.Value.AsyncDatabaseCommands.GetBuildNumber()
				.ContinueOnSuccessInTheUIThread(x => BuildNumber = x.BuildVersion)
				.Catch();
		}

		private void DisplyaLicenseStatus()
		{
			SelectedDatabase.Value.AsyncDatabaseCommands.GetLicenseStatus()
				.ContinueOnSuccessInTheUIThread(x =>
				{
					License.Value = x;
					if (x.Error == false)
						return;
					new ShowLicensingStatusCommand().Execute(x);
				})
				.Catch();
		}

		public Observable<LicensingStatus> License { get; private set; }
	}
}