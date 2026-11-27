using System.Collections.Generic;
using System.Linq;
using LogicSim.Simulation;
using NUnit.Framework;
using UnityEngine;

namespace LogicSim.Tests.App
{
    /// <summary>
    /// The bundled snake (Tools/generate_snake.py) played by a bot through its buttons and screen: after every move the
    /// screen shows exactly the snake the bot expects plus the food, the score counts the eaten foods, and a bite ends
    /// the game.
    /// </summary>
    public class ExampleSnakeTests
    {
        private const int Size = 16;
        private static readonly (int dx, int dy)[] Steps = { (1, 0), (0, 1), (-1, 0), (0, -1) };   // right, down, left, up
        private static readonly string[] Arrows = { "Right", "Down", "Left", "Up" };

        private sealed class Game
        {
            private readonly Circuit _circuit;
            private readonly SimulationSession _session;
            private readonly CircuitComponent _screen;
            private readonly CircuitComponent _chip;
            private readonly int[] _buttons;
            private readonly int _half;
            private readonly byte[] _pixels = new byte[Size * Size];

            public readonly LinkedList<(int x, int y)> Snake = new LinkedList<(int, int)>();
            public (int x, int y) Food = (8, 8);
            public int Direction;
            public int Eaten;

            private int Period => 2 * 4 * 2 * _half;    // two cycles of four phases per move

            public Game()
            {
                var asset = Resources.Load<TextAsset>("Examples/snake");
                Assert.That(asset, Is.Not.Null);
                var library = new SubcircuitLibrary();
                var document = CircuitSerializer.Deserialize(asset.text, library);
                Assert.That(document.Warnings, Is.Empty);
                _circuit = new Circuit(library);
                document.Snapshot.Restore(_circuit);
                _screen = _circuit.Components.Single(c => c.Kind == ComponentKind.Screen);
                _chip = _circuit.Components.Single(c => c.Kind == ComponentKind.Subcircuit);
                _buttons = Arrows.Select(name => _circuit.Components.Single(c => c.Label == name).Id).ToArray();
                _half = _circuit.Components.Single(c => c.Kind == ComponentKind.Clock).ClockHalfPeriod;
                _session = new SimulationSession(_circuit, new ManagedSimulationEngine());
                _session.Reset();
                Assert.That(_session.Compiled.HasErrors, Is.False);

                // Moves come 15 half periods after the start and every period after that; a move is drawn (head, tail,
                // food) within the following 7 half periods. The bot looks and presses right after that.
                _session.Run(15 * _half + 7 * _half + 3);
                Snake.AddFirst((1, 0));
                AssertScreen("after the first move");
            }

            public void Dispose() => _session.Dispose();

            private int Output(string name)
            {
                int pin = Enumerable.Range(0, _circuit.GetOutputCount(_chip)).Single(p => _circuit.GetOutputName(_chip, p) == name);
                return _session.GetOutputValue(_chip.Id, pin) ? 1 : 0;
            }

            public int Score => Enumerable.Range(0, 4).Sum(bit => Output("SCORE" + bit) << bit);
            public bool Dead => Output("DEAD") != 0;

            public HashSet<(int x, int y)> Lit()
            {
                Assert.That(_session.TryGetScreen(_screen, _pixels), Is.True);
                return new HashSet<(int, int)>(Enumerable.Range(0, _pixels.Length).Where(i => _pixels[i] != 0).Select(i => (i % Size, i / Size)));
            }

            /// <summary>One move in <paramref name="direction"/> (an arrow held for two clocks when it changes).</summary>
            public void Move(int direction, bool expectBite = false)
            {
                if (direction != Direction)
                {
                    _circuit.SetInputValue(_buttons[direction], true);
                    _session.Run(4 * _half);
                    _circuit.SetInputValue(_buttons[direction], false);
                    _session.Run(Period - 4 * _half);
                    Direction = direction;
                }
                else
                {
                    _session.Run(Period);
                }
                if (expectBite)
                    return;
                var head = Snake.First.Value;
                var next = (((head.x + Steps[direction].dx) % Size + Size) % Size, ((head.y + Steps[direction].dy) % Size + Size) % Size);
                Snake.AddFirst(next);
                if (next == Food)
                    Eaten++;
                else
                    Snake.RemoveLast();
                AssertScreen($"move to {next}");
            }

            private void AssertScreen(string when)
            {
                var lit = Lit();
                var extra = lit.Except(Snake).ToList();
                Assert.That(Snake.All(lit.Contains) && extra.Count <= 1, Is.True,
                    $"{when}: snake {string.Join(" ", Snake)}, screen {string.Join(" ", lit)}");
                if (extra.Count == 1)
                    Food = extra[0];
                Assert.That(Score, Is.EqualTo(Eaten), $"{when}: score");
                Assert.That(Dead, Is.False, $"{when}: alive");
            }
        }

        [Test]
        public void ABot_EatsSixFoods_ThenBitesItself()
        {
            var game = new Game();
            try
            {
                // Right until the food's column, then down to it: never a reversal, the edges wrap around.
                int moves = 0;
                while (game.Eaten < 6)
                {
                    Assert.That(moves++, Is.LessThan(400), "the bot finds the food");
                    var head = game.Snake.First.Value;
                    game.Move(head.x != game.Food.x ? 0 : 1);
                }
                Assert.That(game.Snake.Count, Is.EqualTo(7));

                // A clockwise square (right, down, left, up are 0..3) comes back to the cell it started from, which is
                // the fourth segment by then.
                int before = game.Direction;
                for (int k = 0; k < 3; k++)
                    game.Move((before + k) % 4);
                game.Move((before + 3) % 4, expectBite: true);
                Assert.That(game.Dead, Is.True, "GAME OVER");
                var frozen = game.Lit();
                game.Move(game.Direction, expectBite: true);
                game.Move(game.Direction, expectBite: true);
                Assert.That(game.Lit().SetEquals(frozen), Is.True, "nothing moves after the bite");
                Assert.That(game.Score, Is.EqualTo(6));
            }
            finally
            {
                game.Dispose();
            }
        }

        [Test]
        public void AReversalIsIgnored()
        {
            var game = new Game();
            try
            {
                game.Move(0);
                // Moving right, left would reverse: the snake keeps going right.
                var head = game.Snake.First.Value;
                game.Move(2, expectBite: true);
                game.Direction = 0;
                var lit = game.Lit();
                Assert.That(lit.Contains(((head.x + 1) % Size, head.y)), Is.True, "still going right");
                Assert.That(lit.Contains(((head.x - 1 + Size) % Size, head.y)), Is.False, "not turned around");
                Assert.That(game.Dead, Is.False);
            }
            finally
            {
                game.Dispose();
            }
        }
    }
}
