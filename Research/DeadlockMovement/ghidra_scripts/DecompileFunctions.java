// Decompile functions by RVA to C-like text files for local reading. Read only.
// Arguments: output-directory rva [rva...]. Output stays under local/: never commit it.
// @category DeadlockMovement

import java.io.File;
import java.io.FileWriter;

import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;

public class DecompileFunctions extends GhidraScript {

	@Override
	protected void run() throws Exception {
		String[] args = getScriptArgs();
		if (args.length < 2) {
			throw new IllegalArgumentException("Usage: output-directory rva [rva...]");
		}
		File directory = new File(args[0]);
		directory.mkdirs();
		DecompInterface decompiler = new DecompInterface();
		decompiler.openProgram(currentProgram);
		Address base = currentProgram.getImageBase();
		try {
			for (int i = 1; i < args.length; i++) {
				long rva = Long.decode(args[i]);
				Function function = getFunctionContaining(base.add(rva));
				if (function == null) {
					// Vtable targets are not always defined by auto-analysis.
					disassemble(base.add(rva));
					function = createFunction(base.add(rva), null);
				}
				if (function == null) {
					println("No function at RVA " + args[i]);
					continue;
				}
				DecompileResults result = decompiler.decompileFunction(function, 120, monitor);
				String text = result.decompileCompleted()
					? result.getDecompiledFunction().getC()
					: "// decompile failed: " + result.getErrorMessage() + "\n";
				String entry = Long.toHexString(function.getEntryPoint().subtract(base));
				File output = new File(directory, "0x" + entry + ".c");
				try (FileWriter writer = new FileWriter(output)) {
					writer.write("// " + function.getName(true) + " rva=0x" + entry + "\n");
					writer.write(text);
				}
				println("Wrote " + output);
			}
		}
		finally {
			decompiler.dispose();
		}
	}
}
