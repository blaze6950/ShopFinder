namespace ShopFinder
{
    public class Shop
    {
        public string Name { get; set; }
        public string Merch { get; set; }
        public string Address { get; set; }
        public string Description { get; set; }
        public string Category { get; set; }
        public string Link { get; set; }
        public string RRLink { get; set; }
        public int Reviews { get; set; }
        public bool AccessibleFromUa { get; set; } = true;
    }
}
