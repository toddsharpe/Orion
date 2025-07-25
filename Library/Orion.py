
#
# Structs
#
from dataclasses import dataclass

@dataclass
class Func:
    Name: str

@dataclass
class Array:
    Data: list
    Length: int

    def __getitem__(self, idx):
        return self.Data[idx]

    def __setitem__(self, idx, val):
        self.Data[idx] = val

#
# Runtime functions.
#
def WriteLine(s: str) -> None:
    print(s)

def WriteInts(ints: list) -> None:
    print(",".join([str(i) for i in ints]))

def u8_str(i: int) -> str:
    return str(i)

def u16_str(i: int) -> str:
    return str(i)

def u32_str(i: int) -> str:
    return str(i)

def u64_str(i: int) -> str:
    return str(i)

def i8_str(i: int) -> str:
    return str(i)

def i16_str(i: int) -> str:
    return str(i)

def i32_str(i: int) -> str:
    return str(i)

def i64_str(i: int) -> str:
    return str(i)

def bool_str(b: bool) -> str:
    return str(b).lower()

def Assert(b: bool) -> None:
    assert b

def Func_Name(f: Func) -> str:
    return f.Name
