namespace DOL.AI.Brain
{
    public class GuardState_RETURN_TO_SPAWN : StandardMobState_RETURN_TO_SPAWN
    {
        protected override short Speed => _brain.Body.MaxSpeed;

        public GuardState_RETURN_TO_SPAWN(StandardMobBrain brain) : base(brain) { }
    }

    /// <summary>
    /// Keep guards hold their post: past <see cref="KeepGuardBrain.LEASH_RANGE"/>
    /// from the spawn point they drop aggro and return (bug 146). Patrol guards
    /// keep their patrol behaviour; their spawn point is not their post.
    /// </summary>
    public class GuardState_AGGRO : StandardMobState_AGGRO
    {
        public GuardState_AGGRO(StandardMobBrain brain) : base(brain) { }

        public override void Think()
        {
            if (_brain.Body is DOL.GS.Keeps.GameKeepGuard { PatrolGroup: null } guard &&
                KeepGuardBrain.ShouldLeash(guard.GetDistanceTo(guard.SpawnPoint), KeepGuardBrain.LEASH_RANGE))
            {
                _brain.FSM.SetCurrentState(DOL.GS.eFSMStateType.RETURN_TO_SPAWN);
                return;
            }

            base.Think();
        }
    }
}
