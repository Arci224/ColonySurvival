﻿
using Science;

namespace Pipliz.Mods.BaseGame.Researches
{
	[AutoLoadedResearchable]
	public class HealthRegenSize3 : BaseResearchable
	{
		public HealthRegenSize3 ()
		{
			key = "pipliz.baseresearch.healthregensize3";
			icon = "gamedata/textures/icons/baseresearch_healthregensize3.png";
			iterationCount = 50;
			AddIterationRequirement("sciencebagadvanced");
			AddIterationRequirement("sciencebaglife", 3);
			AddDependency("pipliz.baseresearch.healthregensize2");
		}

		public override void OnResearchComplete (ColonyScienceState manager, EResearchCompletionReason reason)
		{
			manager.Colony.TemporaryData.SetAs("pipliz.healthregenmax", 85f);
		}
	}
}
