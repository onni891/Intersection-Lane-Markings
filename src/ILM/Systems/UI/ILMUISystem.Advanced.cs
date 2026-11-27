using System;
using System.Collections.Generic;
using System.IO;
using Game.Net;
using Unity.Entities;
using ILM.Components;
using ILM.Geometry;
using ILM.Systems.Topology;
using ILM.Utilities;

namespace ILM.Systems.UI
{
    // The Advanced tab: copy / paste, saved templates and auto markings.
    public partial class ILMUISystem
    {
        // Copied markings, kept for the session.
        private MarkingTemplate _clipboard;
        // Saved templates, kept in ModsData/ILM/templates.txt.
        private readonly List<MarkingTemplate> _templates = new List<MarkingTemplate>();
        // The last paste, so "Turn" can lay the same template down rotated one road further.
        private Entity _lastPasteNode;
        private MarkingTemplate _lastPasteTemplate;
        private int _lastPasteTurn;
        // Outcome of the last paste for the panel's message (PasteResult), and a tick that
        // changes on every paste so the message shows again.
        private int _pasteResult;
        private int _pasteTick;
        private int _pastedLines;
        private int _pastedAreas;

        private MarkingAutoSystem _auto;

        private static string TemplatesPath
            => Path.Combine(UnityEngine.Application.persistentDataPath, "ModsData", "ILM", "templates.txt");

        private void RegisterAdvancedTriggers()
        {
            _auto = World.GetOrCreateSystemManaged<MarkingAutoSystem>();
            LoadTemplates();

            CreateTrigger("CopyMarkings", OnCopyMarkings);
            CreateTrigger("PasteMarkings", () => Paste(_clipboard, -1));
            CreateTrigger("TurnPaste", OnTurnPaste);
            CreateTrigger<string>("SaveTemplate", OnSaveTemplate);
            CreateTrigger<int>("PasteTemplate", i => { if (i >= 0 && i < _templates.Count) Paste(_templates[i], -1); });
            CreateTrigger<int>("DeleteTemplate", OnDeleteTemplate);
            CreateTrigger<bool>("SetAutoMarkings", on => { Mod.Settings.AutoMarkings = on; Mod.Settings.Apply(); });
            CreateTrigger<int>("SetAutoMarkingStyle", s => { Mod.Settings.AutoMarkingStyle = s; Mod.Settings.Apply(); });
            CreateTrigger<bool>("SetAutoTurnsOnly", on => { Mod.Settings.AutoMarkingTurnsOnly = on; Mod.Settings.Apply(); });
            CreateTrigger<int>("SetAutoMode", OnSetAutoMode);
            CreateTrigger("KeepAutoLines", () => _auto.KeepAutoLines(_tool.SelectedNode));
        }

        // ------------------------------------------------------------------ copy / paste

        private void OnCopyMarkings()
        {
            var node = _tool.SelectedNode;
            if (node == Entity.Null) return;
            _clipboard = MarkingTemplates.Capture(EntityManager, node);
            log.Debug($"UI: copied node#{node.Index} — {_clipboard?.lines.Count ?? 0} line(s), {_clipboard?.areas.Count ?? 0} area(s)");
        }

        private void Paste(MarkingTemplate t, int turn)
        {
            var node = _tool.SelectedNode;
            if (node == Entity.Null) return;
            var r = MarkingTemplates.Apply(EntityManager, node, t, turn, out int used, out _pastedLines, out _pastedAreas);
            _pasteResult = (int)r;
            _pasteTick++;
            if (r == PasteResult.Ok)
            {
                _lastPasteNode = node;
                _lastPasteTemplate = t;
                _lastPasteTurn = used;
                _tool.OnNodeEditedExternally();
                ClearUIHover();
            }
            log.Debug($"UI: paste on node#{node.Index} → {r} (turn {used}, {_pastedLines} line(s), {_pastedAreas} area(s))");
        }

        private void OnTurnPaste()
        {
            if (!CanTurn(_tool.SelectedNode)) return;
            Paste(_lastPasteTemplate, _lastPasteTurn + 1);
        }

        private bool CanTurn(Entity node)
            => node != Entity.Null && node == _lastPasteNode && _lastPasteTemplate != null
               && MarkingTemplates.TurnCount(_lastPasteTemplate) > 1;

        // ------------------------------------------------------------------ templates

        private void OnSaveTemplate(string name)
        {
            var node = _tool.SelectedNode;
            if (node == Entity.Null) return;
            var t = MarkingTemplates.Capture(EntityManager, node);
            if (t == null || t.IsEmpty) return;
            name = (name ?? "").Trim();
            if (name.Length > 40) name = name.Substring(0, 40);
            t.name = name.Length > 0 ? name : DefaultName(t);
            _templates.Add(t);
            SaveTemplates();
            log.Debug($"UI: saved template \"{t.name}\"");
        }

        private string DefaultName(MarkingTemplate t)
        {
            string kind = t.isRoad ? "Road" : $"{t.roadCount}-way junction";
            int n = 1;
            foreach (var other in _templates) if (other.name.StartsWith(kind, StringComparison.Ordinal)) n++;
            return n > 1 ? $"{kind} {n}" : kind;
        }

        private void OnDeleteTemplate(int index)
        {
            if (index < 0 || index >= _templates.Count) return;
            if (_lastPasteTemplate == _templates[index]) _lastPasteTemplate = null;
            _templates.RemoveAt(index);
            SaveTemplates();
        }

        private void LoadTemplates()
        {
            _templates.Clear();
            try
            {
                if (File.Exists(TemplatesPath)) _templates.AddRange(MarkingTemplates.Read(File.ReadAllText(TemplatesPath)));
                log.Info($"ILMUISystem: {_templates.Count} saved template(s)");
            }
            catch (Exception e)
            {
                log.Warn($"ILMUISystem: could not read {TemplatesPath}: {e.Message}");
            }
        }

        private void SaveTemplates()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(TemplatesPath));
                // Write next to it first, so a crash mid-write can't leave a half file.
                string tmp = TemplatesPath + ".tmp";
                File.WriteAllText(tmp, MarkingTemplates.Write(_templates));
                if (File.Exists(TemplatesPath)) File.Delete(TemplatesPath);
                File.Move(tmp, TemplatesPath);
            }
            catch (Exception e)
            {
                log.Warn($"ILMUISystem: could not write {TemplatesPath}: {e.Message}");
            }
        }

        // ------------------------------------------------------------------ auto markings

        private void OnSetAutoMode(int mode)
        {
            var node = _tool.SelectedNode;
            if (node == Entity.Null || _tool.SelectedIsRoad) return;
            _auto.SetMode(node, (AutoMarkingMode)Math.Max(0, Math.Min(2, mode)));
        }

        // ------------------------------------------------------------------ view model

        private int RoadCountOf(Entity node)
        {
            if (node == Entity.Null) return 0;
            if (MarkingEdgeAnchors.IsEdgeOwner(EntityManager, node)) return 1;
            return EntityManager.HasBuffer<ConnectedEdge>(node) ? EntityManager.GetBuffer<ConnectedEdge>(node, isReadOnly: true).Length : 0;
        }

        private static bool Fits(MarkingTemplate t, bool isRoad, int roads)
            => t != null && t.isRoad == isRoad && t.roadCount == roads;

        private void HashAdvanced(ref Fnv1a h, Entity node)
        {
            h.Add(_clipboard != null);
            if (_clipboard != null) { h.Add(_clipboard.lines.Count); h.Add(_clipboard.areas.Count); h.Add(_clipboard.roadCount); }
            h.Add(_templates.Count);
            foreach (var t in _templates) h.Add(t.name.GetHashCode());
            h.Add(_pasteTick);
            h.Add(CanTurn(node));
            h.Add(Mod.Settings.AutoMarkings);
            h.Add(Mod.Settings.AutoMarkingStyle);
            h.Add(Mod.Settings.AutoMarkingTurnsOnly);
            h.Add((int)_auto.GetMode(node));
            h.Add(_auto.CountAutoLines(node));
        }

        private void FillAdvanced(PanelStateVM vm, Entity node)
        {
            bool isRoad = node != Entity.Null && MarkingEdgeAnchors.IsEdgeOwner(EntityManager, node);
            int roads = RoadCountOf(node);
            vm.clipboard = _clipboard == null ? null : ToVM(_clipboard, -1, isRoad, roads);
            vm.templates = new TemplateVM[_templates.Count];
            for (int i = 0; i < _templates.Count; i++) vm.templates[i] = ToVM(_templates[i], i, isRoad, roads);
            vm.pasteResult = _pasteResult;
            vm.pasteTick = _pasteTick;
            vm.pastedLines = _pastedLines;
            vm.pastedAreas = _pastedAreas;
            vm.canTurn = CanTurn(node);
            vm.autoGlobal = Mod.Settings.AutoMarkings;
            vm.autoStyle = Mod.Settings.AutoMarkingStyle;
            vm.autoTurnsOnly = Mod.Settings.AutoMarkingTurnsOnly;
            vm.autoMode = (int)_auto.GetMode(node);
            vm.autoLineCount = _auto.CountAutoLines(node);
        }

        private static TemplateVM ToVM(MarkingTemplate t, int index, bool isRoad, int roads)
            => new TemplateVM
            {
                index = index,
                name = t.name,
                roads = t.roadCount,
                isRoad = t.isRoad,
                lines = t.lines.Count,
                areas = t.areas.Count,
                fits = Fits(t, isRoad, roads),
            };
    }
}
