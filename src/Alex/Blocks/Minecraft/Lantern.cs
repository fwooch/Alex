namespace Alex.Blocks.Minecraft
{
	public class Lantern : Block
	{
		public Lantern()
		{
			Solid = true;
			Transparent = true;

			LightValue = 15;
		}
	}
}