using Alex.Net;
using Alex.Worlds;

namespace Alex.Entities.Passive
{
	public class WaterMob : Mob
	{
		/// <inheritdoc />
		protected WaterMob(EntityType mobTypes, World level) : base(mobTypes, level, null) { }
	}
}