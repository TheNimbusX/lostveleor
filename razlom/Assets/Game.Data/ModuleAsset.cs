using System;
using System.Collections.Generic;
using Game.Sim;
using UnityEngine;

namespace Game.Data
{
    [Serializable]
    public struct ConnectorAsset
    {
        public Vector2Int Cell;
        public Direction Facing;
    }

    [CreateAssetMenu(fileName = "Module", menuName = "Разлом/Локации/Модуль")]
    public sealed class ModuleAsset : ScriptableObject
    {
        [Tooltip("Stable key participates in seeded generation. Renaming changes existing maps.")]
        public string StableKey = "module.new";
        [Range(1, 32)] public int Width = 5;
        [Range(1, 32)] public int Height = 5;
        [Range(0, 10000)] public int Weight = 100;
        public bool IsEntrance;
        public ConnectorAsset[] Connectors = Array.Empty<ConnectorAsset>();

        public ModuleDefinition ToDefinition()
        {
            if (string.IsNullOrWhiteSpace(StableKey)) throw new ArgumentException(name + ": specify a stable key.");
            if (Width < 1 || Width > 32 || Height < 1 || Height > 32 || Weight < 0 || Weight > 10000)
                throw new ArgumentException(name + ": invalid dimensions or weight.");
            if (Connectors == null || Connectors.Length == 0 || Connectors.Length > 8)
                throw new ArgumentException(name + ": supply 1–8 connectors.");
            var connectors = new ModuleConnector[Connectors.Length];
            var used = new HashSet<(int, int, Direction)>();
            for (int i = 0; i < connectors.Length; i++)
            {
                var c = Connectors[i];
                int x = c.Cell.x, y = c.Cell.y;
                bool edge = c.Facing == Direction.North ? y == Height - 1 :
                    c.Facing == Direction.East ? x == Width - 1 :
                    c.Facing == Direction.South ? y == 0 :
                    c.Facing == Direction.West && x == 0;
                if (x < 0 || x >= Width || y < 0 || y >= Height || !edge ||
                    !used.Add((x, y, c.Facing)))
                    throw new ArgumentException(name + ": connector " + (i + 1) + " must face out of an edge and be unique.");
                connectors[i] = new ModuleConnector(x, y, c.Facing);
            }
            return new ModuleDefinition(StableKey, Width, Height, connectors, Weight, IsEntrance);
        }
    }
}
