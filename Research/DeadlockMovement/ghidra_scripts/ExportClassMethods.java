// List the virtual methods of named classes from their RTTI vftables.
// Arguments: report.json Class [Class...]. Read only.
// Slot order comes from the table; method meaning does not. Confirm in the decompiler.
// @category DeadlockMovement

import java.io.FileWriter;
import java.util.*;

import com.google.gson.*;

import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;
import ghidra.program.model.mem.Memory;
import ghidra.program.model.symbol.*;

public class ExportClassMethods extends GhidraScript {

	private static final int MAX_SLOTS = 600;

	@Override
	protected void run() throws Exception {
		String[] args = getScriptArgs();
		if (args.length < 2) {
			throw new IllegalArgumentException("Usage: report.json Class [Class...]");
		}
		Address base = currentProgram.getImageBase();
		Memory memory = currentProgram.getMemory();
		SymbolTable symbols = currentProgram.getSymbolTable();
		JsonObject report = new JsonObject();
		for (int i = 1; i < args.length; i++) {
			String name = args[i];
			JsonArray tables = new JsonArray();
			for (Symbol symbol : symbols.getSymbols("vftable")) {
				String parent = symbol.getParentNamespace().getName(true);
				if (!parent.equals(name)) {
					continue;
				}
				JsonObject table = new JsonObject();
				table.addProperty("symbol", symbol.getName(true));
				table.addProperty("rva", hex(symbol.getAddress().subtract(base)));
				JsonArray slots = new JsonArray();
				Address cursor = symbol.getAddress();
				for (int slot = 0; slot < MAX_SLOTS; slot++) {
					long target = memory.getLong(cursor);
					Address address = toAddr(target);
					Function function = getFunctionAt(address);
					if (function == null) {
						break; // end of table
					}
					JsonObject entry = new JsonObject();
					entry.addProperty("slot", slot);
					entry.addProperty("rva", hex(address.subtract(base)));
					entry.addProperty("name", function.getName(true));
					entry.addProperty("bytes", function.getBody().getNumAddresses());
					slots.add(entry);
					cursor = cursor.add(8);
				}
				table.add("slots", slots);
				tables.add(table);
			}
			report.add(name, tables);
			println(name + ": " + tables.size() + " vftable(s)");
		}
		try (FileWriter writer = new FileWriter(args[0])) {
			new GsonBuilder().setPrettyPrinting().create().toJson(report, writer);
		}
	}

	private static String hex(long value) {
		return "0x" + Long.toHexString(value);
	}
}
