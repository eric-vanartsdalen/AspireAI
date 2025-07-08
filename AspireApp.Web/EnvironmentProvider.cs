namespace AspireApp.Web;

public class EnvironmentProvider
{
    public IWebHostEnvironment Environment { get; }

    public EnvironmentProvider(IWebHostEnvironment environment)
    {
        Environment = environment;
    }
}