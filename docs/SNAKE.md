# Snake example

`Assets/Resources/Examples/snake.json` (Open > Examples > *Snake*) is a snake game built from gates and flip-flops:
665 gates in one chip, a 16 × 16 screen, a score display and a GAME OVER lamp. `Tools/generate_snake.py` writes it.

## Playing

Start the simulation (F5). The arrow keys steer: the four push buttons are bound to them (their *Key* in the
inspector), so they work while the canvas has the focus. The snake starts in the top left corner going right, the food
starts at (8, 8). Eating makes the snake one cell longer and moves the food to a pseudo-random cell; the edges wrap
around. Biting itself lights GAME OVER and stops the game; stop and start the simulation (F5 twice) to play again.

At the default 600 ticks per second the snake makes about four moves per second. `]` and `[` double and halve the
simulation speed, and with it the game's.

## How it works

The chip (double-click it to look inside, also while the game runs) has five parts.

| Part | What it is |
|---|---|
| Body | 17 position registers P0 .. P16 (8 D flip-flops each: x and y, 4 bits each). On every move P0 takes the next head and every Pi takes P(i-1). |
| Length | LEN, 4 flip-flops: the number of eaten foods (0-15), shown as the score. The snake is P0 .. P(LEN); P(LEN + 1) is the cell it just left. |
| Direction | The arrow pressed last is latched on every clock (PEND0-1), copied to REQ at the start of phase 2 and taken by DIR at the next move, unless it is the reverse of the current direction. |
| Food | 8 flip-flops; when the next head is on the food they load an 8-bit LFSR that runs on every clock (XNOR feedback from bits 8, 6, 5 and 4). The register starts at 0 and is shown with (8, 8) added, so the first food is not under the head. |
| Screen port | The screen is the picture memory. Every clock cycle has four phases (a 2-bit counter): phase 1 writes the head (D = 1), phase 2 clears the cell the tail left (D = 0), phase 3 draws the food (D = 1). Nothing else is ever drawn or erased. |

A move happens every second cycle: MOVE, a flip-flop that rises at the start of a cycle, clocks the body, the length,
the direction, the food and DEAD. The next head is the head plus or minus one in x or y (two 4-bit adders that add 1
or all ones, so the edges wrap). It bites itself when it lands on P1 .. P(LEN - 1) (14 comparators, each enabled while
LEN is long enough); P0 is where it comes from and P(LEN) moves away. The tail cell is P(LEN + 1), picked by a
one-hot decoder of LEN and an OR of the selected bits.

## Timing

Everything is clocked; the longest paths decide the clock (half period 10 ticks):

- Screen writes: WR rises 2 ticks after the clock falls, in phases 1-3. The address settles at most 9 ticks after the
  rising edge that changed the phase or the registers.
- Moves: the direction feeds the next head (a 4-bit ripple adder), the comparators and DEAD, about 25 ticks deep.
  That is why REQ is sampled two clocks (40 ticks) before a move instead of on the last clock. PEND still catches a
  key on every clock, so short taps are not lost.

`ExampleSnakeTests` plays the game through the buttons and the screen: a bot eats six foods (checking after every move
that the screen shows exactly the snake and the food, and that the score counts), then turns in a tight square and
bites itself; a reversal is ignored.
