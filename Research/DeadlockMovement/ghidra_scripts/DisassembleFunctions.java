// Print the instructions of functions by RVA, for cases the decompiler simplifies away
// (tail calls, register arguments). Arguments: output-directory rva [rva...]. Read only.
// @category DeadlockMovement

import java.io.File;
import java.io.FileWriter;

import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;
import ghidra.program.model.listing.Instruction;
import ghidra.program.model.listing.InstructionIterator;

public class DisassembleFunctions extends GhidraScript {

	@Override
	protected void run() throws Exception {
		String[] args = getScriptArgs();
		File directory = new File(args[0]);
		directory.mkdirs();
		Address base = currentProgram.getImageBase();
		for (int i = 1; i < args.length; i++) {
			Address entry = base.add(Long.decode(args[i]));
			Function function = getFunctionContaining(entry);
			if (function == null) {
				disassemble(entry);
				function = createFunction(entry, null);
			}
			if (function == null) {
				println("No function at RVA " + args[i]);
				continue;
			}
			StringBuilder out = new StringBuilder("; " + function.getName() + "\n");
			InstructionIterator instructions = currentProgram.getListing().getInstructions(function.getBody(), true);
			while (instructions.hasNext()) {
				Instruction instruction = instructions.next();
				out.append(String.format("0x%x  %s%n", instruction.getAddress().subtract(base), instruction));
			}
			File output = new File(directory, args[i] + ".asm");
			try (FileWriter writer = new FileWriter(output)) {
				writer.write(out.toString());
			}
			println("Wrote " + output);
		}
	}
}
