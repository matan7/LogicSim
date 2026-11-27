"""Generates the snake game example of LogicSim: Assets/Resources/Examples/snake.json.

A snake on a 16 x 16 screen, built from gates and flip-flops in one chip. The arrow keys steer it (push buttons bound
to the keys), it grows by eating the food and dies when it bites itself; the edges wrap around. Restart by stopping and
starting the simulation (F5 twice).

How it works (see docs/SNAKE.md):
- The body is a shift register of positions P0 (the head) .. P16. On every move P0 takes the next head and every Pi
  takes P(i-1); LEN (0-15) is the number of eaten foods, so the snake is P0 .. P(LEN) and P(LEN + 1) is the cell it
  just left.
- The screen is the picture memory: a clock cycle has four phases, and phases 1, 2 and 3 write the head (1), the cell
  the tail left (0) and the food (1). Nothing else is ever drawn or erased.
- A move happens every 2^SPEED_BITS cycles. The game registers are clocked by MOVE, a flip-flop that rises at the start
  of a cycle. The arrow pressed last is latched on every clock (PEND), copied to REQ at the start of phase 2 and taken
  at the next move (a reversal is ignored): REQ is two clocks old at a move, so the next head and the bite check have
  settled.
- The food is a register; when the head reaches it, it takes the value of an 8-bit LFSR that runs on every clock.

Usage: python Tools/generate_snake.py [output.json]
Plain Python 3, no packages; shares the circuit helpers of generate_8bit_computer.py.
"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from generate_8bit_computer import Circuit, Definition, Instance, layered_layout  # noqa: E402

HALF_PERIOD = 10    # ticks; the next head and the bite check need about 25 ticks after the direction changes
SPEED_BITS = 1      # a move every 2^SPEED_BITS cycles of four phases
SIZE_BITS = 4       # 16 x 16 cells
SEGMENTS = 16       # longest snake: P0 .. P15 plus P16, the cell the tail left
FOOD_START = 0x88   # the food starts at (8, 8); the register itself starts at 0


def build():
    Definition.registry = {}
    n = SIZE_BITS
    pos = [f"X{i}" for i in range(n)] + [f"Y{i}" for i in range(n)]
    w = len(pos)

    # ---------------------------------------------------------------------------------------------------------------
    # Building blocks
    fa = Definition("full-adder", "Full adder", ["A", "B", "Cin"], ["S", "Cout"])
    c = fa.c
    x1 = c.gate("Xor", [fa.pin_in["A"], fa.pin_in["B"]])
    fa.out("S", c.gate("Xor", [x1, fa.pin_in["Cin"]]))
    fa.out("Cout", c.gate("Or", [c.gate("And", [fa.pin_in["A"], fa.pin_in["B"]]), c.gate("And", [x1, fa.pin_in["Cin"]])]))

    # A coordinate one step on: A + 1 with INC, A - 1 (adding all ones) with DEC, wrapping around.
    step = Definition("step", "Step", [f"A{i}" for i in range(n)] + ["INC", "DEC"], [f"S{i}" for i in range(n)])
    c = step.c
    low = c.gate("Or", [step.pin_in["INC"], step.pin_in["DEC"]])
    carry = None
    for i in range(n):
        adder = Instance(c, fa)
        adder.connect("A", step.pin_in[f"A{i}"])
        adder.connect("B", low if i == 0 else step.pin_in["DEC"])
        if carry is None:
            zero = c.add("Constant", "0", value=False)
            adder.connect("Cin", (zero, 0))
        else:
            adder.connect("Cin", carry)
        step.out(f"S{i}", adder.o("S"))
        carry = adder.o("Cout")

    # A position register: X and Y bits, clocked together.
    reg = Definition("position", "Position", pos + ["CLK"], [f"Q{p}" for p in pos])
    c = reg.c
    for p in pos:
        ff = c.add("DFlipFlop")
        c.wire(reg.pin_in[p], (ff, 0))
        c.wire(reg.pin_in["CLK"], (ff, 1))
        reg.out(f"Q{p}", (ff, 0))

    # Two positions are the same cell.
    eq = Definition("same-cell", "Same cell", [f"A{p}" for p in pos] + [f"B{p}" for p in pos], ["EQ"])
    c = eq.c
    bits = [c.gate("Xnor", [eq.pin_in[f"A{p}"], eq.pin_in[f"B{p}"]]) for p in pos]
    eq.out("EQ", c.gate("And", bits))

    # One bit of the position selected by a one-hot select (OR of the selected inputs).
    sel_n = SEGMENTS
    mux = Definition("select-bit", "Select bit", [f"D{i}" for i in range(sel_n)] + [f"S{i}" for i in range(sel_n)], ["Y"])
    c = mux.c
    ands = [c.gate("And", [mux.pin_in[f"D{i}"], mux.pin_in[f"S{i}"]]) for i in range(sel_n)]
    groups = [c.gate("Or", ands[k:k + 8]) for k in range(0, sel_n, 8)]
    mux.out("Y", groups[0] if len(groups) == 1 else c.gate("Or", groups))

    # ---------------------------------------------------------------------------------------------------------------
    # The game chip
    outputs = [f"SX{i}" for i in range(n)] + [f"SY{i}" for i in range(n)] + ["D", "WR"] + [f"SCORE{i}" for i in range(4)] + ["DEAD"]
    game = Definition("snake-chip", "Snake", ["CLK", "RIGHT", "DOWN", "LEFT", "UP"], outputs)
    c = game.c
    clk = game.pin_in["CLK"]
    one = (c.add("Constant", "1", value=True), 0)

    # Phase counter (synchronous): PH0 toggles on every clock, PH1 when PH0 is 1.
    ph0_ff = c.add("TFlipFlop", "PH0")
    c.wire(one, (ph0_ff, 0))
    c.wire(clk, (ph0_ff, 1))
    ph1_ff = c.add("TFlipFlop", "PH1")
    c.wire((ph0_ff, 0), (ph1_ff, 0))
    c.wire(clk, (ph1_ff, 1))
    ph0, ph1 = (ph0_ff, 0), (ph1_ff, 0)
    last_phase = c.gate("And", [ph0, ph1], "phase 3")

    # Cycle counter: counts at the end of every cycle; a move follows the cycle where it is all ones.
    carry = last_phase
    cyc = []
    for i in range(SPEED_BITS):
        t = c.add("TFlipFlop", f"CYC{i}")
        c.wire(carry, (t, 0))
        c.wire(clk, (t, 1))
        cyc.append((t, 0))
        carry = c.gate("And", [carry, (t, 0)])

    # MOVE rises with the clock that starts the next cycle and clocks the game registers.
    dead_ff = c.add("DFlipFlop", "DEAD")
    alive = c.gate("Not", [(dead_ff, 0)], "alive")
    move_ff = c.add("DFlipFlop", "MOVE")
    c.wire(c.gate("And", [carry, alive], "move next"), (move_ff, 0))
    c.wire(clk, (move_ff, 1))
    move = (move_ff, 0)

    # The arrow pressed last, latched on every clock while one is held: right 00, down 01, left 10, up 11.
    right, down, left, up = (game.pin_in[k] for k in ("RIGHT", "DOWN", "LEFT", "UP"))
    any_key = c.gate("Or", [right, down, left, up], "arrow")
    no_key = c.gate("Not", [any_key])
    enc = [c.gate("Or", [down, up], "dir bit 0"), c.gate("Or", [left, up], "dir bit 1")]
    nph0, nph1 = c.gate("Not", [ph0]), c.gate("Not", [ph1])
    at_head = c.gate("And", [ph0, nph1], "phase 1")
    # SAMPLE rises at the start of phase 2: REQ takes PEND two clocks before the next move.
    sample_ff = c.add("DFlipFlop", "SAMPLE")
    c.wire(at_head, (sample_ff, 0))
    c.wire(clk, (sample_ff, 1))
    req = []
    for i in range(2):
        pend = c.add("DFlipFlop", f"PEND{i}")
        keep = c.gate("And", [(pend, 0), no_key])
        take = c.gate("And", [enc[i], any_key])
        c.wire(c.gate("Or", [keep, take]), (pend, 0))
        c.wire(clk, (pend, 1))
        ff = c.add("DFlipFlop", f"REQ{i}")
        c.wire((pend, 0), (ff, 0))
        c.wire((sample_ff, 0), (ff, 1))
        req.append((ff, 0))

    # Direction: the requested one unless it is the reverse (same axis bit 0, other bit 1).
    dir_ff = [c.add("DFlipFlop", f"DIR{i}") for i in range(2)]
    cur = [(f, 0) for f in dir_ff]
    reverse = c.gate("And", [c.gate("Xnor", [req[0], cur[0]]), c.gate("Xor", [req[1], cur[1]])], "reverse")
    forward = c.gate("Not", [reverse])
    nxt = []
    for i in range(2):
        d = c.gate("Or", [c.gate("And", [reverse, cur[i]]), c.gate("And", [forward, req[i]])], f"next dir {i}")
        c.wire(d, (dir_ff[i], 0))
        c.wire(move, (dir_ff[i], 1))
        nxt.append(d)
    n0, n1 = c.gate("Not", [nxt[0]]), c.gate("Not", [nxt[1]])

    # Body: P0 is the head.
    regs = [Instance(c, reg, f"P{i}") for i in range(SEGMENTS + 1)]
    for r in regs:
        r.connect("CLK", move)
    head = {p: regs[0].o(f"Q{p}") for p in pos}

    # Next head: X steps on for right (00) and left (10), Y for down (01) and up (11).
    stepper = {}
    for axis, inc, dec in (("X", c.gate("And", [n0, n1], "right"), c.gate("And", [n0, nxt[1]], "left")),
                           ("Y", c.gate("And", [nxt[0], n1], "down"), c.gate("And", [nxt[0], nxt[1]], "up"))):
        s = Instance(c, step, f"next {axis}")
        for i in range(n):
            s.connect(f"A{i}", head[f"{axis}{i}"])
        s.connect("INC", inc)
        s.connect("DEC", dec)
        stepper[axis] = s
    next_head = {f"{axis}{i}": stepper[axis].o(f"S{i}") for axis in "XY" for i in range(n)}
    for p in pos:
        regs[0].connect(p, next_head[p])
    for i in range(1, SEGMENTS + 1):
        for p in pos:
            regs[i].connect(p, regs[i - 1].o(f"Q{p}"))

    # Food: its register starts at 0 and is shown with FOOD_START flipped in, so the first food is not at the head.
    food_ff = {}
    food = {}
    for k, p in enumerate(pos):
        ff = c.add("DFlipFlop", f"F{p}")
        food_ff[p] = ff
        food[p] = c.gate("Not", [(ff, 0)]) if (FOOD_START >> k) & 1 else (ff, 0)
    eat = Instance(c, eq, "eats")
    for p in pos:
        eat.connect(f"A{p}", next_head[p])
        eat.connect(f"B{p}", food[p])
    eats = eat.o("EQ")

    # LFSR (8 bits, XNOR feedback, taps 8 6 5 4): a new pseudo-random value on every clock.
    lfsr = [c.add("DFlipFlop", f"R{i}") for i in range(8)]
    fb = c.gate("Xnor", [(lfsr[7], 0), (lfsr[5], 0), (lfsr[4], 0), (lfsr[3], 0)], "feedback")
    for i, ff in enumerate(lfsr):
        c.wire(fb if i == 0 else (lfsr[i - 1], 0), (ff, 0))
        c.wire(clk, (ff, 1))
    not_eats = c.gate("Not", [eats])
    for k, p in enumerate(pos):
        ff = food_ff[p]
        keep = c.gate("And", [(ff, 0), not_eats])
        new = c.gate("And", [(lfsr[k % 8], 0), eats])
        c.wire(c.gate("Or", [keep, new]), (ff, 0))
        c.wire(move, (ff, 1))

    # Length: LEN counts the eaten foods (up to 15) and is the score.
    len_ff = [c.add("DFlipFlop", f"LEN{i}") for i in range(4)]
    length = [(f, 0) for f in len_ff]
    full = c.gate("And", length, "full")
    grow = c.gate("And", [eats, c.gate("Not", [full])], "grow")
    carry = grow
    for i in range(4):
        c.wire(c.gate("Xor", [length[i], carry]), (len_ff[i], 0))
        c.wire(move, (len_ff[i], 1))
        carry = c.gate("And", [length[i], carry])
    for i in range(4):
        game.out(f"SCORE{i}", length[i])

    # One-hot length: L[j] is 1 when LEN = j.
    nlen = [c.gate("Not", [b]) for b in length]
    is_len = [c.gate("And", [length[b] if (j >> b) & 1 else nlen[b] for b in range(4)], f"len {j}") for j in range(16)]

    # The cell the tail left: P(LEN + 1).
    tail = {}
    for p in pos:
        m = Instance(c, mux, f"tail {p}")
        for j in range(SEGMENTS):
            m.connect(f"D{j}", regs[j + 1].o(f"Q{p}"))
            m.connect(f"S{j}", is_len[j])
        tail[p] = m.o("Y")

    # Biting itself: the next head is on P1 .. P(LEN - 1) (P0 is where it comes from, P(LEN) moves away).
    # longer[i] is 1 when LEN > i.
    longer = {14: is_len[15]}
    for i in range(13, 0, -1):
        longer[i] = c.gate("Or", [longer[i + 1], is_len[i + 1]])
    bites = []
    for i in range(1, 15):
        hit = Instance(c, eq, f"bites P{i}")
        for p in pos:
            hit.connect(f"A{p}", next_head[p])
            hit.connect(f"B{p}", regs[i].o(f"Q{p}"))
        bites.append(c.gate("And", [hit.o("EQ"), longer[i]]))
    bite = c.gate("Or", [c.gate("Or", bites[:7]), c.gate("Or", bites[7:])], "bite")
    c.wire(c.gate("Or", [(dead_ff, 0), bite]), (dead_ff, 0))
    c.wire(move, (dead_ff, 1))
    game.out("DEAD", (dead_ff, 0))

    # Screen port: phase 1 writes the head (D = 1), phase 2 the cell the tail left (D = 0), phase 3 the food (D = 1).
    at_tail = c.gate("And", [nph0, ph1], "phase 2")
    at_food = last_phase
    for p in pos:
        bit = c.gate("Or", [c.gate("And", [at_head, head[p]]), c.gate("And", [at_tail, tail[p]]), c.gate("And", [at_food, food[p]])])
        game.out(f"S{p}", bit)
    game.out("D", ph0)
    # WR rises when the clock falls, in phases 1-3; the phase bits change only after the clock rose.
    game.out("WR", c.gate("And", [c.gate("Not", [clk]), c.gate("Or", [ph0, ph1])], "WR"))

    # ---------------------------------------------------------------------------------------------------------------
    # Top level: clock, arrows, the chip, the screen, the score and GAME OVER
    top = Circuit()
    clock = top.add("Clock", "clock", -14, 0, halfPeriod=HALF_PERIOD)
    keys = {}
    for i, (name, key) in enumerate((("RIGHT", "Right"), ("DOWN", "Down"), ("LEFT", "Left"), ("UP", "Up"))):
        keys[name] = top.add("Button", name.capitalize(), -14, -8 - 8 * i, hotkey=key)
    chip = Instance(top, game, "Snake", x=0, y=0)
    chip.connect("CLK", (clock, 0))
    for name, b in keys.items():
        chip.connect(name, (b, 0))
    screen = top.add("Screen", "Snake screen", 16, 0, width=1 << n, height=1 << n, pixelSize=3)
    for i in range(n):
        top.wire(chip.o(f"SX{i}"), (screen, i))
        top.wire(chip.o(f"SY{i}"), (screen, n + i))
    top.wire(chip.o("D"), (screen, 2 * n))
    top.wire(chip.o("WR"), (screen, 2 * n + 1))
    score = top.add("HexDisplay", "Score", 16, -56)
    for i in range(4):
        top.wire(chip.o(f"SCORE{i}"), (score, i))
    over = top.add("Bulb", "GAME OVER", 26, -56)
    top.wire(chip.o("DEAD"), (over, 0))

    for d in Definition.registry.values():
        layered_layout(d.c, Definition.registry)

    order = ["full-adder", "step", "position", "same-cell", "select-bit", "snake-chip"]
    return {
        "format": "logicsim",
        "version": 1,
        "name": "Snake",
        "circuit": top.json(),
        "subcircuits": [Definition.registry[i].json() for i in order],
    }


DEFAULT_OUTPUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets", "Resources", "Examples", "snake.json")

if __name__ == "__main__":
    doc = build()
    text = json.dumps(doc, indent=1)
    with open(sys.argv[1] if len(sys.argv) > 1 else DEFAULT_OUTPUT, "w", newline="\n") as f:
        f.write(text + "\n")
    inside = {d["id"]: len(d["circuit"]["components"]) for d in doc["subcircuits"]}
    print("top", len(doc["circuit"]["components"]), "components;", "definitions", inside, "bytes", len(text))
