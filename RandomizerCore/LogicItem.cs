﻿using RandomizerCore.Logic;
using RandomizerCore.LogicItems;

namespace RandomizerCore
{
    /// <summary>
    /// Interface used by items and itemlikes (transitions, waypoints).
    /// </summary>
    public interface ILogicItem
    {
        string Name { get; }

        /// <summary>
        /// Directly adds the item to the pm. This does not invoke the pm events, so it is best to implement this explicitly and use pm.Add instead.
        /// </summary>
        void AddTo(ProgressionManager pm);

        /// <summary>
        /// Returns the pm indices potentially modified by the item.
        /// </summary>
        IEnumerable<Term> GetAffectedTerms();
    }

    /// <summary>
    /// Interface which indicates the item has an action once its location is determined.
    /// <r/>The terms modified by the action should be reported in GetAffectedTerms.
    /// </summary>
    public interface ILocationDependentItem : ILogicItem
    {
        /// <summary>
        /// Directly performs the action of the location-dependent item on the pm.
        /// This does not invoke pm events, so consumers should invoke the corresponding method on the pm.
        /// </summary>
        void Place(ProgressionManager pm, ILogicDef location);
    }

    public interface IRemovableItem
    {
        void RemoveFrom(ProgressionManager pm);
    }

    /// <summary>
    /// Interface representing an item which may not have any effect. Items which do not implement this interface
    /// are assumed to unconditionally have an effect.
    /// </summary>
    public interface IConditionalItem
    {
        /// <summary>
        /// Returns false if <see cref="LogicItem.AddTo(ProgressionManager)"/> will not modify the ProgressionManager, otherwise returns true.
        /// </summary>
        bool CheckForEffect(ProgressionManager pm);
        /// <summary>
        /// Equivalent to calling <see cref="IConditionalItem.CheckForEffect(ProgressionManager)"/>, then <see cref="LogicItem.AddTo(ProgressionManager)"/>, and returning the result of the former.
        /// </summary>
        bool TryAddTo(ProgressionManager pm);
    }

    public abstract record LogicItem(string Name) : ILogicItem, ILogicItemTemplate
    {
        public abstract void AddTo(ProgressionManager pm);

        /// <summary>
        /// Returns the terms potentially modified by the item.
        /// </summary>
        public abstract IEnumerable<Term> GetAffectedTerms();

        LogicItem ILogicItemTemplate.Create(LogicManager lm)
        {
            return this;
        }
    }
}
