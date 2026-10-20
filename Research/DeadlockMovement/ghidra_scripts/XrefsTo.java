// List functions that reference each given RVA (data or code). Read only.
// Arguments: report.txt rva [rva...].
// @category DeadlockMovement

import java.io.FileWriter;
import java.util.*;

import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;
import ghidra.program.model.symbol.Reference;

public class XrefsTo extends GhidraScript {

	// Globals are often referenced at small offsets from their start (ConVar handles are 16 bytes apart).
	private static final int SPAN = 0x10;

	@Override
	protected void run() throws Exception {
		String[] args = getScriptArgs();
		Address base = currentProgram.getImageBase();
		StringBuilder out = new StringBuilder();
		for (int i = 1; i < args.length; i++) {
			Address target = base.add(Long.decode(args[i]));
			Map<String, Integer> functions = new TreeMap<>();
			for (int delta = 0; delta < SPAN; delta++) {
				for (Reference ref : getReferencesTo(target.add(delta))) {
					Function function = getFunctionContaining(ref.getFromAddress());
					String key = function == null ? "?@0x" + Long.toHexString(ref.getFromAddress().subtract(base))
						: function.getName() + "@0x" + Long.toHexString(function.getEntryPoint().subtract(base))
							+ " (" + function.getBody().getNumAddresses() + " bytes)";
					functions.merge(key + " +0x" + Integer.toHexString(delta), 1, Integer::sum);
				}
			}
			out.append("== ").append(args[i]).append('\n');
			functions.forEach((key, count) -> out.append("  ").append(key).append(" x").append(count).append('\n'));
		}
		try (FileWriter writer = new FileWriter(args[0])) {
			writer.write(out.toString());
		}
		println("Wrote " + args[0]);
	}
}
