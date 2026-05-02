namespace Acmeshd;

internal static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            return AcmeshdApp.Run(args);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("acmeshd: stopped");

            return 130;
        }
        catch (Exception ex)
        {
            Console.WriteLine("acmeshd: error: " + ex.Message);

            return 1;
        }
    }
}
