namespace CarryOn
{
    class CarryOnClientConfig
    {
        public bool HoldControlForBackSwapFocus = true;

        public CarryOnClientConfig()
        {
        }

        public CarryOnClientConfig(CarryOnClientConfig previousConfig)
        {
            HoldControlForBackSwapFocus = previousConfig.HoldControlForBackSwapFocus;
        }
    }
}