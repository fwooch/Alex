namespace Alex.Blocks.Minecraft
{
	public class Portal : Block
	{
		public Portal() : base()
		{
			Solid = false;
			Transparent = true;
			IsReplacible = false;
			Animated = true;
			
			LightValue = 11;

			BlockMaterial = Material.Portal;
			
			Hardness = 60000;
		}
	}
}
