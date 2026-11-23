//Every case is its own process with its own scratch dir, so nothing is shared to race on.
[assembly: Parallelize(Workers = 0, Scope = ExecutionScope.MethodLevel)]
