﻿using Newtonsoft.Json;
using RandomizerCore.Extensions;
using RandomizerCore.Json;
using RandomizerCore.Logic.StateLogic;
using RandomizerCore.StringLogic;
using System.Collections.ObjectModel;

namespace RandomizerCore.Logic
{
    [JsonConverter(typeof(LMConverter))]
    public class LogicManager : ILogicManager
    {
        public readonly TermCollection Terms;
        public readonly ReadOnlyDictionary<string, LogicDef> LogicLookup;
        public readonly ReadOnlyDictionary<string, LogicItem> ItemLookup;
        public readonly ReadOnlyDictionary<string, LogicTransition> TransitionLookup;
        public readonly ReadOnlyCollection<LogicWaypoint> Waypoints;
        public readonly ReadOnlyCollection<LogicVariable> Variables;
        public readonly StateManager StateManager;

        // Data structures dynamically constructed to correspond to logic
        private readonly Dictionary<string, LogicDef> _logicDefs;
        public LogicProcessor LP { get; }
        private readonly List<LogicVariable> _variables;
        private readonly Dictionary<string, int> _variableIndices;
        private readonly Dictionary<string, LogicItem> _items;
        private readonly Dictionary<string, LogicTransition> _transitions;
        private readonly LogicManagerBuilder _source; // set to null on exiting constructor
        private readonly DNFConverter _DNFConverter;
        private readonly List<int> _logicBuilder;

        public VariableResolver VariableResolver { get; }

        public const int intVariableOffset = -100;

        public LogicManager(LogicManagerBuilder source)
        {
            _source = source;
            LP = source.LP;
            VariableResolver = source.VariableResolver;
            StateManager = new(source.StateManager);
            _DNFConverter = new();
            _logicBuilder = new();

            // Terms
            Terms = new(source.Terms);

            // Variables
            VariableResolver = source.VariableResolver ?? new VariableResolver();
            _variables = new();
            _variableIndices = new();
            Variables = new(_variables);

            // Logic
            _logicDefs = new(source.LogicLookup.Count);
            foreach (KeyValuePair<string, LogicClause> kvp in source.LogicLookup)
            {
                _logicDefs.Add(kvp.Key, CreateDNFLogicDef(kvp.Key, kvp.Value));
            }
            LogicLookup = new(_logicDefs);

            // Waypoints
            Waypoints = new(source.Waypoints.Select(name => new LogicWaypoint(Terms.GetTerm(name), _logicDefs[name] as StateLogicDef ?? CreateDNFLogicDef(new(name, _logicDefs[name].InfixSource)))).ToArray());

            // Transitions
            _transitions = source.Transitions.ToDictionary(name => name, name => new LogicTransition(_logicDefs[name] as StateLogicDef ?? CreateDNFLogicDef(new(name, _logicDefs[name].InfixSource)), Terms.GetTerm(name)));
            TransitionLookup = new(_transitions);

            // Items
            _items = new(source.UnparsedItems.Count + source.TemplateItems.Count + source.PrefabItems.Count);
            JsonSerializer js = JsonUtil.GetLogicSerializer(this);
            foreach (var kvp in source.UnparsedItems)
            {
                _items.Add(kvp.Key, kvp.Value.ToObject<LogicItem>(js));
            }
            foreach (var kvp in source.TemplateItems)
            {
                _items.Add(kvp.Key, kvp.Value.Create(this));
            }
            foreach (var kvp in source.PrefabItems)
            {
                _items[kvp.Key] = kvp.Value;
            }
            ItemLookup = new(_items);

            _source = null;
            _DNFConverter.Trim();
            _logicBuilder.TrimExcess();
        }

        /// <summary>
        /// Fetches the logic def by name. Returns null if not defined.
        /// </summary>
        public LogicDef? GetLogicDef(string name)
        {
            if (!_logicDefs.TryGetValue(name, out LogicDef def))
            {
                return null;
            }

            return def;
        }

        /// <summary>
        /// Fetches the logic def by name.
        /// </summary>
        /// <exception cref="ArgumentException">The logic def is not defined.</exception>
        public LogicDef GetLogicDefStrict(string name)
        {
            return GetLogicDef(name) ?? throw new ArgumentException($"LogicDef {name} is not defined.");
        }

        /// <summary>
        /// Fetches the term by name. Returns null if not defined.
        /// </summary>
        public Term? GetTerm(string item)
        {
            if (Terms.GetTerm(item) is not Term t)
            {
                return null;
            }

            return t;
        }

        /// <summary>
        /// Fetches the term by name. Returns null if not defined.
        /// </summary>
        /// <exception cref="ArgumentException">The tern is not defined.</exception>
        public Term GetTermStrict(string item)
        {
            return Terms.GetTerm(item) ?? throw new ArgumentException($"Term {item} is not defined.");
        }

        /// <summary>
        /// Fetches the term by its id.
        /// </summary>
        public Term GetTerm(int id)
        {
            return Terms[id];
        }

        /// <summary>
        /// Fetches the variable by its id.
        /// </summary>
        public LogicVariable GetVariable(int id)
        {
            return _variables[intVariableOffset - id];
        }

        /// <summary>
        /// Fetches the variable by name. If the variable has not yet been instantiated, caches it and gives it an id. Returns null if the name cannot be resolved to a variable.
        /// </summary>
        public LogicVariable? GetVariable(string name)
        {
            if (GetVariableID(name) is int id) return GetVariable(id);
            else return null;
        }

        /// <summary>
        /// Fetches the variable by name. If the variable has not yet been instantiated, caches it and gives it an id.
        /// </summary>
        /// <exception cref="ArgumentException">The name cannot be resolved to a variable.</exception>
        public LogicVariable GetVariableStrict(string name)
        {
            if (GetVariableID(name) is int id) return GetVariable(id);
            throw new ArgumentException($"Unable to resolve {name} to LogicVariable.");
        }

        private int? GetVariableID(string name)
        {
            if (_variableIndices.TryGetValue(name, out int index)) return index;

            LogicVariable lv;
            try
            {
                if (!VariableResolver.TryMatch(this, name, out lv)) return null;
                if (lv is null) throw new NullReferenceException($"Parsed {name} to null LogicVariable!");
            }
            catch (Exception e)
            {
                throw new ArgumentException($"Error parsing {name} to LogicVariable.", e);
            }

            index = intVariableOffset - _variables.Count;
            _variableIndices.Add(name, index);
            _variables.Add(lv);
            return index;
        }

        /// <summary>
        /// Fetches the logic item by name. Returns null if not defined.
        /// </summary>
        public LogicItem? GetItem(string name)
        {
            if (!_items.TryGetValue(name, out LogicItem item))
            {
                return null;
            }

            return item;
        }

        /// <summary>
        /// Fetches the logic item by name.
        /// </summary>
        /// <exception cref="ArgumentException">The logic item is not defined.</exception>
        public LogicItem GetItemStrict(string name)
        {
            return GetItem(name) ?? throw new ArgumentException($"LogicItem {name} is not defined.");
        }

        /// <summary>
        /// Fetches the logic transition by name. Returns null if not defined.
        /// </summary>
        public LogicTransition? GetTransition(string name)
        {
            if (!_transitions.TryGetValue(name, out LogicTransition transition))
            {
                return null;
            }

            return transition;
        }

        /// <summary>
        /// Fetches the logic transition by name.
        /// </summary>
        /// <exception cref="ArgumentException">The logic transition is not defined.</exception>
        public LogicTransition GetTransitionStrict(string name)
        {
            return GetTransition(name) ?? throw new ArgumentException($"LogicTransition {name} is not defined.");
        }

        public LogicDef FromString(RawLogicDef def)
        {
            return CreateDNFLogicDef(def);
        }

        public RPNLogicDef CreateRPNLogicDef(RawLogicDef def)
        {
            return Process(def);
        }

        private RPNLogicDef Process(RawLogicDef def)
        {
            try
            {
                return FromTokens(def.name, LP.ParseInfixToList(def.logic));
            }
            catch (Exception e)
            {
                throw new ArgumentException($"Error in processing logic for {def.name}.", e);
            }
        }

        [Obsolete]
        public RPNLogicDef FromTokens(string name, LogicClause c)
        {
            List<int> logic = new();
            try
            {
                for (int i = 0; i < c.Count; i++)
                {
                    ApplyToken(logic, c[i]);
                }
            }
            catch (Exception e)
            {
                throw new ArgumentException($"Error in processing logic for {name}.", e);
            }

            return new(name, logic.ToArray(), this, c.ToInfix());
        }

        private RPNLogicDef FromTokens(string name, IReadOnlyList<LogicToken> lts)
        {
            List<int> logic = new();
            try
            {
                foreach (LogicToken lt in lts)
                {
                    ApplyToken(logic, lt);
                }
            }
            catch (Exception e)
            {
                throw new ArgumentException($"Error in processing logic for {name}.", e);
            }

            return new(name, logic.ToArray(), this, Infix.ToInfix(lts));
        }

        public DNFLogicDef CreateDNFLogicDef(RawLogicDef def) => CreateDNFLogicDef(def.name, def.logic, LP.ParseInfixToList(def.logic));

        public DNFLogicDef CreateDNFLogicDef(string name, LogicClause tokens) => CreateDNFLogicDef(name, tokens.ToInfix(), tokens.Tokens.ToList());

        private DNFLogicDef CreateDNFLogicDef(string name, string infix, List<LogicToken> tokens)
        {
            try
            {
                Expand(tokens);
                _DNFConverter.Convert(tokens);
                var res = _DNFConverter.Result;
                DNFLogicDef.Clause[] clauses = new DNFLogicDef.Clause[res.Count];
                for (int j = 0; j < clauses.Length; j++)
                {
                    _logicBuilder.Clear();
                    foreach (TermToken tt in res[j])
                    {
                        ApplyToken(_logicBuilder, tt);
                    }
                    int[] logic = _logicBuilder.Where(i => i > intVariableOffset || GetVariable(i) is not StateModifier).ToArray();
                    StateModifier[] sms = _logicBuilder.Where(i => i <= intVariableOffset && GetVariable(i) is StateModifier).Select(i => (StateModifier)GetVariable(i)).ToArray();
                    int source = -1;
                    for (int i = 0; i < logic.Length; i++)
                    {
                        if (logic[i] >= 0 && Terms[logic[i]].Type == TermType.State || logic[i] <= intVariableOffset && GetVariable(logic[i]) is StateProvider)
                        {
                            source = logic[i];
                            break;
                        }
                    }
                    clauses[j] = new() { logic = logic, stateModifiers = sms, stateProvider = source, };
                }
                clauses.StableSort((c1, c2) => c1.stateModifiers.Length - c2.stateModifiers.Length);
                _logicBuilder.Clear();
                return new(clauses, this, name, Infix.ToInfix(tokens));
            }
            catch (Exception e)
            {
                throw new ArgumentException($"Error creating logic def for {name} with logic {infix}.", e);
            }
        }

        private void Expand(List<LogicToken> tokens)
        {
            for (int i = 0; i < tokens.Count; i++)
            {
                switch (tokens[i])
                {
                    case ReferenceToken rt:
                        tokens.RemoveAt(i);
                        if (_source != null && _source.LogicLookup.TryGetValue(rt.Target, out LogicClause lc))
                        {
                            tokens.InsertRange(i, lc.Tokens);
                        }
                        else if (_logicDefs.TryGetValue(rt.Target, out LogicDef def))
                        {
                            tokens.InsertRange(i, def.ToTokenSequence());
                        }
                        else throw new ArgumentException($"ReferenceToken {rt.Write()} points to undefined logic.");
                        i--;
                        break;
                    case MacroToken mt:
                        tokens.RemoveAt(i);
                        tokens.InsertRange(i, mt.Value.Tokens);
                        i--;
                        break;
                    case CoalescingToken qt:
                        tokens[i] = IsValidToken(qt.Left) ? qt.Left : qt.Right;
                        i--;
                        break;
                }
            }
        }

        private void ApplyToken(List<int> logic, LogicToken lt)
        {
            switch (lt)
            {
                case OperatorToken ot:
                    ApplyOperatorToken(logic, ot);
                    break;
                case SimpleToken st:
                    ApplySimpleToken(logic, st);
                    break;
                case ComparisonToken ct:
                    ApplyComparisonToken(logic, ct);
                    break;
                case ConstToken bt:
                    ApplyConstToken(logic, bt);
                    break;
                case MacroToken mt:
                    ApplyMacroToken(logic, mt);
                    break;
                case ReferenceToken rt:
                    ApplyReferenceToken(logic, rt);
                    break;
                case CoalescingToken qt:
                    ApplyCoalescingToken(logic, qt);
                    break;
                default:
                    throw new ArgumentException($"Found unrecognized token in logic: {lt}");
            }
        }

        private void ApplyOperatorToken(List<int> logic, OperatorToken ot)
        {
            logic.Add(ot.OperatorType switch
            {
                OperatorType.AND => (int)LogicOperators.AND,
                OperatorType.OR => (int)LogicOperators.OR,
                _ => throw new NotImplementedException()
            });
        }

        private void ApplySimpleToken(List<int> logic, SimpleToken st)
        {
            ApplyTermOrVariable(logic, st.Name);
        }

        private void ApplyComparisonToken(List<int> logic, ComparisonToken ct)
        {
            logic.Add(ct.ComparisonType switch
            {
                ComparisonType.EQ => (int)LogicOperators.EQ,
                ComparisonType.LT => (int)LogicOperators.LT,
                ComparisonType.GT => (int)LogicOperators.GT,
                _ => throw new NotImplementedException(),
            });
            ApplyTermOrVariable(logic, ct.Left);
            ApplyTermOrVariable(logic, ct.Right);
        }

        private void ApplyConstToken(List<int> logic, ConstToken bt)
        {
            logic.Add(bt.Value ? (int)LogicOperators.ANY : (int)LogicOperators.NONE);
        }

        private void ApplyMacroToken(List<int> logic, MacroToken mt)
        {
            foreach (LogicToken tt in mt.Value) ApplyToken(logic, tt);
        }

        private void ApplyReferenceToken(List<int> logic, ReferenceToken rt)
        {
            if (_source != null && _source.LogicLookup.TryGetValue(rt.Target, out LogicClause lc))
            {
                foreach (LogicToken tt in lc) ApplyToken(logic, tt);
            }
            else if (_logicDefs.TryGetValue(rt.Target, out LogicDef def))
            {
                foreach (LogicToken tt in def.ToTokenSequence()) ApplyToken(logic, tt);
            }
            else throw new ArgumentException($"ReferenceToken {rt.Write()} points to undefined logic.");
        }

        private void ApplyCoalescingToken(List<int> logic, CoalescingToken qt)
        {
            if (IsValidToken(qt.Left)) ApplyToken(logic, qt.Left);
            else ApplyToken(logic, qt.Right);
        }

        private bool IsValidToken(LogicToken lt)
        {
            return lt switch
            {
                OperatorToken => true,
                SimpleToken st => IsTermOrVariable(st.Name),
                ComparisonToken ct => IsTermOrVariable(ct.Left) && IsTermOrVariable(ct.Right),
                ConstToken => true,
                MacroToken mt => mt.Source?.GetMacro(mt.Name) is not null,
                CoalescingToken qt => IsValidToken(qt.Left) || IsValidToken(qt.Right),
                _ => false,
            };
        }

        private bool IsTermOrVariable(string name)
        {
            return name != null && (Terms.IsTerm(name) || _variableIndices.ContainsKey(name) || VariableResolver.CanMatch(this, name));
        }

        private void ApplyTermOrVariable(List<int> logic, string name)
        {
            if (Terms.GetTerm(name) is Term t)
            {
                logic.Add(t.Id);
            }
            else if (GetVariableID(name) is int variableID)
            {
                logic.Add(variableID);
            }
            else throw new ArgumentException($"Unknown string {name} found as term.");
        }
    }
}
