namespace StreamCommand.Services
{
    public static class FeatureGate
    {
        public static bool Pro => EntitlementService.IsPro;

        public static bool Has(string feature)
        {
            return Pro;
        }
    }
}
