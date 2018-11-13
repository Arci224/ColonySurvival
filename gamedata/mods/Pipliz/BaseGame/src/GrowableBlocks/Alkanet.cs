﻿using BlockEntities;
using GrowableBlocks;
using System.Collections.Generic;

namespace Pipliz.Mods.BaseGame.GrowableBlocks
{
	[BlockEntityAutoLoader]
	public class Alkanet : BaseGrowableBlockDefinition
	{
		public Alkanet ()
		{
			GrowthType = EGrowthType.FirstNightRandom;
			SetStages(new List<GrowableStage>()
			{
				new GrowableStage("alkanetstage1"),
				new GrowableStage("alkanetstage2")
			});
		}
	}
}
