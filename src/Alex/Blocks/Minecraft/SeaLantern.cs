namespace Alex.Blocks.Minecraft
{
	public class SeaLantern : Block
	{
		public SeaLantern() : base()
		{
			Solid = true;
			Transparent = false;
			IsReplacible = false;
			LightValue = 15;
			
			Hardness = 0.3f;

			BlockMaterial = Material.Glass;
		}
	}
}
