using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.Hosting;

namespace AspireApp.WebTest.Factories;

sealed class DashboardEnabledAspireAppHostFactory() : DistributedApplicationFactory(typeof(Program))
{
	public DistributedApplication? Application { get; private set; }

	protected override void OnBuilderCreating(DistributedApplicationOptions applicationOptions, HostApplicationBuilderSettings hostOptions)
	{
		base.OnBuilderCreating(applicationOptions, hostOptions);
		applicationOptions.DisableDashboard = false;
		applicationOptions.AllowUnsecuredTransport = true;
	}

	protected override void OnBuilt(DistributedApplication application)
	{
		base.OnBuilt(application);
		Application = application;
	}
}
