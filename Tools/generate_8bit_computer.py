"""Generates the 8-bit computer example of LogicSim: Assets/Resources/Examples/8-bit-computer.json.

Accumulator machine built from gates and D flip-flops only: 8-bit bus, registers A, B, IR and OUT, 4-bit PC and MAR,
an adder/subtractor ALU, a five-step ring counter and the control unit in a CPU chip, next to a 16 x 8 RAM chip that
starts with the program below. An instruction is an opcode (high nibble) and an operand (low nibble). See
docs/8BIT_COMPUTER.md.

Usage: python Tools/generate_8bit_computer.py [output.json]
Change PROGRAM (or HALF_PERIOD) and run it again; plain Python 3, no packages.
"""
import json, os, sys

OPCODES = {"NOP": 0x0, "LDA": 0x1, "ADD": 0x2, "SUB": 0x3, "STA": 0x4, "LDI": 0x5, "JMP": 0x6, "OUT": 0xE, "HLT": 0xF}

# Fibonacci: prints 0 1 1 2 3 5 8 13 21 ... (8 bits, wraps) on the OUT register.
PROGRAM = [
    ("LDI", 1), ("STA", 14), ("LDI", 0), ("STA", 15),       # x = 1, y = 0
    ("OUT", 0), ("ADD", 14), ("STA", 13),                   # 4: out y; t = y + x
    ("LDA", 14), ("STA", 15), ("LDA", 13), ("STA", 14),     # y = x; x = t
    ("LDA", 15), ("JMP", 4),                                # A = y; loop
    0, 0, 0,                                                # 13: t, 14: x, 15: y
]

HALF_PERIOD = 20


def assemble(program):
    out = []
    for item in program:
        out.append(item if isinstance(item, int) else (OPCODES[item[0]] << 4) | (item[1] & 0xF))
    assert len(out) == 16
    return out


# ---------------------------------------------------------------------------------------------------------------
class Circuit:
    def __init__(self):
        self.components = []
        self.wires = []
        self.by_id = {}

    def add(self, type_, label=None, x=None, y=None, **props):
        cid = len(self.components) + 1
        c = {"id": cid, "type": type_, "x": x, "y": y}
        if label is not None:
            c["label"] = label
        c.update(props)
        self.components.append(c)
        self.by_id[cid] = c
        return cid

    def wire(self, src, dst):
        (sc, sp), (dc, dp) = src, dst
        for w in self.wires:
            assert not (w["to"]["component"] == dc and w["to"]["pin"] == dp), f"input {dc}.{dp} driven twice"
        self.wires.append({"id": len(self.wires) + 1, "from": {"component": sc, "pin": sp}, "to": {"component": dc, "pin": dp}})

    def gate(self, type_, inputs, label=None, x=None, y=None):
        g = self.add(type_, label, x, y, inputs=len(inputs)) if type_ not in ("Not", "Buffer") else self.add(type_, label, x, y)
        for i, src in enumerate(inputs):
            self.wire(src, (g, i))
        return (g, 0)

    def json(self):
        comps = []
        for c in self.components:
            c = dict(c)
            c["x"] = float(c["x"] or 0)
            c["y"] = float(c["y"] or 0)
            comps.append(c)
        return {"components": comps, "wires": self.wires}


class Definition:
    """An IC definition: named input and output pins, ordered top to bottom."""
    registry = {}

    def __init__(self, ident, name, inputs, outputs):
        self.ident, self.name = ident, name
        self.inputs, self.outputs = list(inputs), list(outputs)
        self.c = Circuit()
        self.pin_in = {}
        self.pin_out = {}
        for i, n in enumerate(self.inputs):
            self.pin_in[n] = (self.c.add("PinIn", n, 0, -6 * i, value=False), 0)
        for i, n in enumerate(self.outputs):
            self.pin_out[n] = (self.c.add("PinOut", n, None, -6 * i), 0)
        Definition.registry[ident] = self

    def out(self, name, src):
        self.c.wire(src, self.pin_out[name])

    def json(self):
        return {"id": self.ident, "name": self.name, "circuit": self.c.json()}


class Instance:
    def __init__(self, circuit, definition, label=None, x=None, y=None):
        self.c, self.d = circuit, definition
        self.id = circuit.add("Subcircuit", label, x, y, subcircuit=definition.ident)

    def i(self, name):
        return (self.id, self.d.inputs.index(name))

    def o(self, name):
        return (self.id, self.d.outputs.index(name))

    def connect(self, name, src):
        self.c.wire(src, self.i(name))


# ---------------------------------------------------------------------------------------------------------------
# Sizes as LogicSim draws them (generic box: width 6, height 2 * rows + 2).
def size(c, defs):
    t = c["type"]
    if t in ("Constant", "Clock", "Button", "PinIn", "PinOut"):
        return 4, 4
    if t in ("Switch", "Bulb", "Not", "Buffer"):
        return 5, 4
    if t in ("And", "Or", "Xor", "Nand", "Nor", "Xnor"):
        n = c.get("inputs", 2)
        return (5, 4) if n == 2 else (6, 2 * n + 2)
    if t == "DFlipFlop":
        return 6, 6
    if t == "HexDisplay":
        return 6, 10
    if t == "Subcircuit":
        d = defs[c["subcircuit"]]
        return 6, 2 * max(1, len(d.inputs), len(d.outputs)) + 2
    return 6, 6


def layered_layout(circuit, defs, column_width=14, gap=2):
    """Places components without a position in columns by their depth from the inputs (flip-flops break loops)."""
    preds = {c["id"]: [] for c in circuit.components}
    for w in circuit.wires:
        preds[w["to"]["component"]].append(w["from"]["component"])
    depth = {}

    def d(cid, stack=()):
        if cid in depth:
            return depth[cid]
        c = circuit.by_id[cid]
        if c["type"] in ("PinIn", "Constant", "Clock", "Switch", "Button") or cid in stack:
            depth[cid] = 0
            return 0
        best = 0
        for p in preds[cid]:
            pc = circuit.by_id[p]
            if pc["type"] == "DFlipFlop" and c["type"] != "DFlipFlop":
                continue    # feedback from a flip-flop: the consumer does not wait for it
            best = max(best, d(p, stack + (cid,)) + 1)
        depth[cid] = best
        return best

    for c in circuit.components:
        d(c["id"])
    last = max(depth.values()) + 1
    for c in circuit.components:
        if c["type"] == "PinOut":
            depth[c["id"]] = last
    columns = {}
    for c in circuit.components:
        if c["x"] is None:
            columns.setdefault(depth[c["id"]], []).append(c)
    for col, items in columns.items():
        y = 0
        for c in items:
            w, h = size(c, defs)
            c["x"] = col * column_width
            if c["y"] is None or c["type"] != "PinOut":
                c["y"] = y
            y -= h + gap
    for c in circuit.components:
        if c["x"] is None:
            c["x"] = depth[c["id"]] * column_width
        if c["y"] is None:
            c["y"] = 0


# ---------------------------------------------------------------------------------------------------------------
def build():
    Definition.registry = {}
    bits8 = [f"D{i}" for i in range(8)]

    # Full adder
    fa = Definition("full-adder", "Full adder", ["A", "B", "Cin"], ["S", "Cout"])
    c = fa.c
    x1 = c.gate("Xor", [fa.pin_in["A"], fa.pin_in["B"]])
    fa.out("S", c.gate("Xor", [x1, fa.pin_in["Cin"]]))
    a1 = c.gate("And", [fa.pin_in["A"], fa.pin_in["B"]])
    a2 = c.gate("And", [x1, fa.pin_in["Cin"]])
    fa.out("Cout", c.gate("Or", [a1, a2]))

    # Registers with load enable: D = LOAD ? in : Q
    def register(ident, name, n):
        r = Definition(ident, name, [f"D{i}" for i in range(n)] + ["LOAD", "CLK"], [f"Q{i}" for i in range(n)])
        c = r.c
        nload = c.gate("Not", [r.pin_in["LOAD"]])
        for i in range(n):
            ff = c.add("DFlipFlop", f"ff{i}")
            take = c.gate("And", [r.pin_in[f"D{i}"], r.pin_in["LOAD"]])
            keep = c.gate("And", [(ff, 0), nload])
            c.wire(c.gate("Or", [take, keep]), (ff, 0))
            c.wire(r.pin_in["CLK"], (ff, 1))
            r.out(f"Q{i}", (ff, 0))
        return r

    reg8 = register("reg8", "Register 8-bit", 8)
    reg4 = register("reg4", "Register 4-bit", 4)

    # Bus drivers (what tri-state buffers do on a real bus): out = in AND OE
    def driver(ident, name, n):
        dr = Definition(ident, name, [f"D{i}" for i in range(n)] + ["OE"], [f"O{i}" for i in range(n)])
        for i in range(n):
            dr.out(f"O{i}", dr.c.gate("And", [dr.pin_in[f"D{i}"], dr.pin_in["OE"]]))
        return dr

    drv8 = driver("driver8", "Bus driver 8-bit", 8)
    drv4 = driver("driver4", "Bus driver 4-bit", 4)

    # ALU: S = A + (B xor SUB) + SUB
    alu = Definition("alu8", "ALU 8-bit (add/sub)", [f"A{i}" for i in range(8)] + [f"B{i}" for i in range(8)] + ["SUB"],
                     [f"S{i}" for i in range(8)] + ["CARRY"])
    c = alu.c
    carry = alu.pin_in["SUB"]
    for i in range(8):
        b = c.gate("Xor", [alu.pin_in[f"B{i}"], alu.pin_in["SUB"]])
        f = Instance(c, fa, f"FA{i}")
        f.connect("A", alu.pin_in[f"A{i}"])
        f.connect("B", b)
        f.connect("Cin", carry)
        alu.out(f"S{i}", f.o("S"))
        carry = f.o("Cout")
    alu.out("CARRY", carry)

    # Program counter: next = J ? D : Q + CE
    pc = Definition("pc4", "Program counter 4-bit", [f"D{i}" for i in range(4)] + ["J", "CE", "CLK"], [f"Q{i}" for i in range(4)])
    c = pc.c
    nj = c.gate("Not", [pc.pin_in["J"]])
    carry = pc.pin_in["CE"]
    for i in range(4):
        ff = c.add("DFlipFlop", f"ff{i}")
        inc = c.gate("Xor", [(ff, 0), carry])
        nxt_carry = c.gate("And", [(ff, 0), carry])
        load = c.gate("And", [pc.pin_in[f"D{i}"], pc.pin_in["J"]])
        count = c.gate("And", [inc, nj])
        c.wire(c.gate("Or", [load, count]), (ff, 0))
        c.wire(pc.pin_in["CLK"], (ff, 1))
        pc.out(f"Q{i}", (ff, 0))
        carry = nxt_carry

    # Step counter: one-hot T0..T4 (T0 when the four flip-flops are 0, so it starts at T0)
    ring = Definition("steps", "Step counter T0-T4", ["CLK"], [f"T{i}" for i in range(5)])
    c = ring.c
    ffs = [c.add("DFlipFlop", f"t{i + 1}") for i in range(4)]
    t0 = c.gate("Nor", [(f, 0) for f in ffs], "T0")
    prev = t0
    for f in ffs:
        c.wire(prev, (f, 0))
        c.wire(ring.pin_in["CLK"], (f, 1))
        prev = (f, 0)
    ring.out("T0", t0)
    for i, f in enumerate(ffs):
        ring.out(f"T{i + 1}", (f, 0))

    # Control logic (microcode as gates)
    signals = ["HLT", "MI", "RI", "RO", "IO", "II", "AI", "AO", "EO", "SU", "BI", "OI", "CE", "CO", "J"]
    ctl = Definition("control", "Control unit", [f"OP{i}" for i in range(4)] + [f"T{i}" for i in range(5)], signals)
    c = ctl.c
    op = [ctl.pin_in[f"OP{i}"] for i in range(4)]
    nop = [c.gate("Not", [o]) for o in op]
    T = [ctl.pin_in[f"T{i}"] for i in range(5)]
    dec = {}
    for name in ["LDA", "ADD", "SUB", "STA", "LDI", "JMP", "OUT", "HLT"]:
        code = OPCODES[name]
        dec[name] = c.gate("And", [op[b] if code >> b & 1 else nop[b] for b in range(4)], name)
    memop = c.gate("Or", [dec["LDA"], dec["ADD"], dec["SUB"], dec["STA"]], "mem")
    arith = c.gate("Or", [dec["ADD"], dec["SUB"]], "arith")
    load_a = c.gate("Or", [dec["LDA"], dec["ADD"], dec["SUB"]], "readmem")
    operand = c.gate("Or", [memop, dec["LDI"], dec["JMP"]], "operand")

    def AND(*xs):
        return c.gate("And", list(xs))

    def OR(*xs):
        return xs[0] if len(xs) == 1 else c.gate("Or", list(xs))

    ctl.out("CO", T[0])
    ctl.out("MI", OR(T[0], AND(T[2], memop)))
    ctl.out("RO", OR(T[1], AND(T[3], load_a)))
    ctl.out("II", c.gate("Buffer", [T[1]]))
    ctl.out("CE", c.gate("Buffer", [T[1]]))
    ctl.out("IO", AND(T[2], operand))
    ctl.out("AI", OR(AND(T[2], dec["LDI"]), AND(T[3], dec["LDA"]), AND(T[4], arith)))
    ctl.out("BI", AND(T[3], arith))
    ctl.out("EO", AND(T[4], arith))
    ctl.out("SU", c.gate("Buffer", [dec["SUB"]]))
    ctl.out("AO", OR(AND(T[2], dec["OUT"]), AND(T[3], dec["STA"])))
    ctl.out("OI", AND(T[2], dec["OUT"]))
    ctl.out("RI", AND(T[3], dec["STA"]))
    ctl.out("J", AND(T[2], dec["JMP"]))
    ctl.out("HLT", AND(T[2], dec["HLT"]))

    # One RAM byte: preset value I (flip-flops start at 0, the byte reads Q xor I), written when WE,
    # read through SEL (the address decoder line).
    rb = Definition("ram-byte", "RAM byte", bits8 + ["WE", "SEL"] + [f"I{i}" for i in range(8)] + ["CLK"], [f"O{i}" for i in range(8)])
    c = rb.c
    reg = Instance(c, reg8, "cells")
    for i in range(8):
        reg.connect(f"D{i}", c.gate("Xor", [rb.pin_in[f"D{i}"], rb.pin_in[f"I{i}"]]))
    reg.connect("LOAD", rb.pin_in["WE"])
    reg.connect("CLK", rb.pin_in["CLK"])
    for i in range(8):
        value = c.gate("Xor", [reg.o(f"Q{i}"), rb.pin_in[f"I{i}"]])
        rb.out(f"O{i}", c.gate("And", [value, rb.pin_in["SEL"]]))

    # RAM 16 x 8 with the program as constants (one row of eight per byte, bit 7 on the left)
    ram = Definition("ram16", "RAM 16 x 8", bits8 + [f"A{i}" for i in range(4)] + ["WE", "OE", "CLK"], [f"O{i}" for i in range(8)])
    c = ram.c
    image = assemble(PROGRAM)
    na = [c.gate("Not", [ram.pin_in[f"A{b}"]], x=12, y=-60 - 6 * b) for b in range(4)]
    bytes_ = []
    for k in range(16):
        row = -k * 42
        sel = c.gate("And", [ram.pin_in[f"A{b}"] if k >> b & 1 else na[b] for b in range(4)], f"sel{k}", x=24, y=row - 10)
        we = c.gate("And", [sel, ram.pin_in["WE"]], f"we{k}", x=34, y=row - 10)
        byte = Instance(c, rb, f"M{k}", x=90, y=row)
        for i in range(8):
            byte.connect(f"D{i}", ram.pin_in[f"D{i}"])
            bit = image[k] >> i & 1
            const = c.add("Constant", f"m{k}.{i}", 46 + (7 - i) * 5, row - 14, value=bool(bit))
            byte.connect(f"I{i}", (const, 0))
        byte.connect("WE", we)
        byte.connect("SEL", sel)
        byte.connect("CLK", ram.pin_in["CLK"])
        bytes_.append(byte)
    for i in range(8):
        lo = c.gate("Or", [b.o(f"O{i}") for b in bytes_[:8]], x=110, y=-i * 44)
        hi = c.gate("Or", [b.o(f"O{i}") for b in bytes_[8:]], x=110, y=-i * 44 - 20)
        both = c.gate("Or", [lo, hi], x=120, y=-i * 44 - 8)
        ram.out(f"O{i}", c.gate("And", [both, ram.pin_in["OE"]], x=130, y=-i * 44 - 8))
    for n in ram.outputs:
        cid = ram.pin_out[n][0]
        ram.c.by_id[cid]["x"] = 142

    # ------------------------------------------------------------------------------------------------------------
    # Processor: registers, ALU, bus and control. It talks to the RAM through DIN/DOUT, ADDR and WE/OE.
    cpu_outputs = ([f"DOUT{i}" for i in range(8)] + [f"ADDR{i}" for i in range(4)] + ["WE", "OE"]
                   + [f"OUT{i}" for i in range(8)] + [f"PC{i}" for i in range(4)] + [f"T{i}" for i in range(5)] + ["HLT"])
    cpu = Definition("cpu", "CPU 8-bit", ["CLK"] + [f"DIN{i}" for i in range(8)], cpu_outputs)
    T_ = cpu.c
    CLK = cpu.pin_in["CLK"]

    ctl_i = Instance(T_, ctl, "Control", x=24, y=-150)
    steps_i = Instance(T_, ring, "Steps", x=8, y=-150)
    steps_i.connect("CLK", CLK)

    pc_i = Instance(T_, pc, "PC", x=30, y=0)
    mar_i = Instance(T_, reg4, "MAR", x=30, y=-26)
    ir_i = Instance(T_, reg8, "IR", x=30, y=-90)
    pc_drv = Instance(T_, drv4, "PC out", x=44, y=0)
    ir_drv = Instance(T_, drv4, "IR out", x=44, y=-90)

    a_i = Instance(T_, reg8, "A", x=120, y=0)
    alu_i = Instance(T_, alu, "ALU", x=120, y=-30)
    b_i = Instance(T_, reg8, "B", x=120, y=-72)
    out_i = Instance(T_, reg8, "OUT", x=120, y=-100)
    a_drv = Instance(T_, drv8, "A out", x=134, y=0)
    alu_drv = Instance(T_, drv8, "ALU out", x=134, y=-30)

    # The bus: each bit is the OR of the enabled drivers (the RAM gates its output with OE itself)
    bus = []
    for i in range(8):
        srcs = [cpu.pin_in[f"DIN{i}"], a_drv.o(f"O{i}"), alu_drv.o(f"O{i}")]
        if i < 4:
            srcs += [pc_drv.o(f"O{i}"), ir_drv.o(f"O{i}")]
        bus.append(T_.gate("Or", srcs, f"bus{i}", x=80, y=-i * 13))

    for i in range(4):
        pc_i.connect(f"D{i}", bus[i])
        mar_i.connect(f"D{i}", bus[i])
        pc_drv.connect(f"D{i}", pc_i.o(f"Q{i}"))
        ir_drv.connect(f"D{i}", ir_i.o(f"Q{i}"))
    for i in range(8):
        ir_i.connect(f"D{i}", bus[i])
        a_i.connect(f"D{i}", bus[i])
        b_i.connect(f"D{i}", bus[i])
        out_i.connect(f"D{i}", bus[i])
        alu_i.connect(f"A{i}", a_i.o(f"Q{i}"))
        alu_i.connect(f"B{i}", b_i.o(f"Q{i}"))
        a_drv.connect(f"D{i}", a_i.o(f"Q{i}"))
        alu_drv.connect(f"D{i}", alu_i.o(f"S{i}"))
    for inst in (pc_i, mar_i, ir_i, a_i, b_i, out_i):
        inst.connect("CLK", CLK)

    for i in range(5):
        ctl_i.connect(f"T{i}", steps_i.o(f"T{i}"))
    for i in range(4):
        ctl_i.connect(f"OP{i}", ir_i.o(f"Q{i + 4}"))
    sig = ctl_i.o
    pc_i.connect("J", sig("J"))
    pc_i.connect("CE", sig("CE"))
    pc_drv.connect("OE", sig("CO"))
    mar_i.connect("LOAD", sig("MI"))
    ir_i.connect("LOAD", sig("II"))
    ir_drv.connect("OE", sig("IO"))
    a_i.connect("LOAD", sig("AI"))
    a_drv.connect("OE", sig("AO"))
    alu_i.connect("SUB", sig("SU"))
    alu_drv.connect("OE", sig("EO"))
    b_i.connect("LOAD", sig("BI"))
    out_i.connect("LOAD", sig("OI"))

    for i in range(8):
        cpu.out(f"DOUT{i}", bus[i])
        cpu.out(f"OUT{i}", out_i.o(f"Q{i}"))
    for i in range(4):
        cpu.out(f"ADDR{i}", mar_i.o(f"Q{i}"))
        cpu.out(f"PC{i}", pc_i.o(f"Q{i}"))
    for i in range(5):
        cpu.out(f"T{i}", steps_i.o(f"T{i}"))
    cpu.out("WE", sig("RI"))
    cpu.out("OE", sig("RO"))
    cpu.out("HLT", sig("HLT"))
    for n in cpu.outputs:
        cpu.c.by_id[cpu.pin_out[n][0]]["x"] = 176

    def hex_pair(circuit, label, getter, x, y):
        lo = circuit.add("HexDisplay", f"{label} low", x + 8, y)
        hi = circuit.add("HexDisplay", f"{label} high", x, y)
        for i in range(4):
            circuit.wire(getter(i), (lo, i))
            circuit.wire(getter(i + 4), (hi, i))

    def hex_one(circuit, label, getter, x, y):
        h = circuit.add("HexDisplay", label, x, y)
        for i in range(4):
            circuit.wire(getter(i), (h, i))

    # Displays inside the CPU (shown when it is opened from the running computer)
    hex_one(T_, "PC", lambda i: pc_i.o(f"Q{i}"), 52, 6)
    hex_one(T_, "MAR", lambda i: mar_i.o(f"Q{i}"), 44, -26)
    hex_pair(T_, "Bus", lambda i: bus[i], 80, 14)
    hex_pair(T_, "IR", lambda i: ir_i.o(f"Q{i}"), 52, -90)
    hex_pair(T_, "A", lambda i: a_i.o(f"Q{i}"), 146, 0)
    hex_pair(T_, "ALU", lambda i: alu_i.o(f"S{i}"), 146, -30)
    hex_pair(T_, "B", lambda i: b_i.o(f"Q{i}"), 146, -72)
    hex_pair(T_, "Output", lambda i: out_i.o(f"Q{i}"), 146, -100)

    # ------------------------------------------------------------------------------------------------------------
    # Top level: clock control, the CPU and the RAM, and the displays
    top = Circuit()
    clock = top.add("Clock", "clock", 0, 0, halfPeriod=HALF_PERIOD)
    run = top.add("Switch", "Run", 0, -8, value=True)
    step = top.add("Button", "Step", 0, -16)
    clk_run = top.gate("And", [(clock, 0), (run, 0)], x=10, y=0)
    clk_any = top.gate("Or", [clk_run, (step, 0)], x=18, y=-6)
    cpu_i = Instance(top, cpu, "CPU", x=44, y=0)
    ram_i = Instance(top, ram, "RAM", x=74, y=0)
    nhlt = top.gate("Not", [cpu_i.o("HLT")], x=18, y=-16)
    clk = top.gate("And", [clk_any, nhlt], "CLK", x=28, y=-8)
    halted = top.add("Bulb", "HALT", 18, -24)
    top.wire(cpu_i.o("HLT"), (halted, 0))
    cpu_i.connect("CLK", clk)
    ram_i.connect("CLK", clk)
    for i in range(8):
        ram_i.connect(f"D{i}", cpu_i.o(f"DOUT{i}"))
        cpu_i.connect(f"DIN{i}", ram_i.o(f"O{i}"))
    for i in range(4):
        ram_i.connect(f"A{i}", cpu_i.o(f"ADDR{i}"))
    ram_i.connect("WE", cpu_i.o("WE"))
    ram_i.connect("OE", cpu_i.o("OE"))

    hex_pair(top, "Bus", lambda i: cpu_i.o(f"DOUT{i}"), 60, 16)
    hex_pair(top, "Output", lambda i: cpu_i.o(f"OUT{i}"), 58, -36)
    hex_one(top, "PC", lambda i: cpu_i.o(f"PC{i}"), 58, -50)
    for i in range(5):
        lamp = top.add("Bulb", f"T{i}", 44 + 6 * i, -74)
        top.wire(cpu_i.o(f"T{i}"), (lamp, 0))

    # Internal layouts
    for d in Definition.registry.values():
        layered_layout(d.c, Definition.registry)
    layered_layout(top, Definition.registry)

    order = ["full-adder", "reg8", "reg4", "driver8", "driver4", "alu8", "pc4", "steps", "control", "ram-byte", "ram16", "cpu"]
    doc = {
        "format": "logicsim",
        "version": 1,
        "name": "8-bit computer",
        "circuit": top.json(),
        "subcircuits": [Definition.registry[i].json() for i in order],
    }
    return doc


DEFAULT_OUTPUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets", "Resources", "Examples", "8-bit-computer.json")

if __name__ == "__main__":
    doc = build()
    s = json.dumps(doc, indent=1)
    with open(sys.argv[1] if len(sys.argv) > 1 else DEFAULT_OUTPUT, "w", newline="\n") as f:
        f.write(s + "\n")
    total = len(doc["circuit"]["components"]) + sum(len(d["circuit"]["components"]) for d in doc["subcircuits"])
    print("top components", len(doc["circuit"]["components"]), "wires", len(doc["circuit"]["wires"]),
          "definitions", len(doc["subcircuits"]), "components in all definitions", total, "bytes", len(s))
    print("program", " ".join(f"{b:02X}" for b in assemble(PROGRAM)))
