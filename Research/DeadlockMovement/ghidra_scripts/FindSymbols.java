// Print symbols and defined strings whose text contains any argument (case-insensitive),
// with RVAs and referencing functions. Arguments: report.txt needle [needle...]. Read only.
// @category DeadlockMovement

import java.io.FileWriter;
import java.util.*;

import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.*;
import ghidra.program.model.symbol.*;

public class FindSymbols extends GhidraScript {

	private static final int MAX_REFS = 12;

	@Override
	protected void run() throws Exception {
		String[] args = getScriptArgs();
		List<String> needles = new ArrayList<>();
		for (int i = 1; i < args.length; i++) {
			needles.add(args[i].toLowerCase());
		}
		Address base = currentProgram.getImageBase();
		StringBuilder out = new StringBuilder();
		for (Symbol symbol : currentProgram.getSymbolTable().getAllSymbols(true)) {
			String full = symbol.getName(true);
			if (matches(full, needles)) {
				out.append("SYM ").append(rva(symbol.getAddress(), base)).append(' ')
					.append(symbol.getSymbolType()).append(' ').append(full).append('\n');
			}
		}
		DataIterator data = currentProgram.getListing().getDefinedData(true);
		while (data.hasNext() && !monitor.isCancelled()) {
			Data item = data.next();
			if (!item.hasStringValue()) {
				continue;
			}
			String text = String.valueOf(item.getValue());
			if (!matches(text, needles)) {
				continue;
			}
			out.append("STR ").append(rva(item.getAddress(), base)).append(" \"")
				.append(text.replace("\n", "\\n")).append("\"");
			int count = 0;
			for (Reference ref : getReferencesTo(item.getAddress())) {
				if (count++ >= MAX_REFS) {
					out.append(" ...");
					break;
				}
				Function function = getFunctionContaining(ref.getFromAddress());
				out.append(" <- ").append(function == null ? rva(ref.getFromAddress(), base)
					: function.getName() + "@" + rva(function.getEntryPoint(), base));
			}
			out.append('\n');
		}
		try (FileWriter writer = new FileWriter(args[0])) {
			writer.write(out.toString());
		}
		println("Wrote " + args[0]);
	}

	private static boolean matches(String text, List<String> needles) {
		String lower = text.toLowerCase();
		for (String needle : needles) {
			if (lower.contains(needle)) {
				return true;
			}
		}
		return false;
	}

	private static String rva(Address address, Address base) {
		return "0x" + Long.toHexString(address.subtract(base));
	}
}
