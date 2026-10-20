// Rank functions by how many distinct fields of each probe struct they read or write.
// Arguments: types.json labels.json report.json. Written by static_analysis.py.
// A match is a memory operand whose displacement equals a field offset. That is a
// lead, not proof: unrelated objects can share an offset. Confirm in the decompiler.
// @category DeadlockMovement

import java.io.FileReader;
import java.io.FileWriter;
import java.util.*;

import com.google.gson.*;

import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.lang.OperandType;
import ghidra.program.model.lang.Register;
import ghidra.program.model.listing.*;
import ghidra.program.model.scalar.Scalar;

public class ProbeFieldUses extends GhidraScript {

	// Small offsets collide with too many unrelated structs to be useful leads.
	private static final int MIN_OFFSET = 0x100;
	private static final int MIN_FIELDS = 2;
	private static final int CALL_DEPTH = 4;

	@Override
	protected void run() throws Exception {
		String[] args = getScriptArgs();
		if (args.length != 3) {
			throw new IllegalArgumentException("Usage: types.json labels.json report.json");
		}
		JsonObject types = read(args[0]);
		Set<String> probes = new HashSet<>();
		for (JsonElement p : types.getAsJsonArray("probes")) {
			probes.add(p.getAsString());
		}
		// offset -> list of "Struct.field"
		Map<Long, List<String[]>> fieldsAt = new HashMap<>();
		for (JsonElement element : types.getAsJsonArray("structs")) {
			JsonObject struct = element.getAsJsonObject();
			String name = struct.get("name").getAsString();
			if (!probes.contains(name)) {
				continue;
			}
			for (JsonElement f : struct.getAsJsonArray("fields")) {
				JsonObject field = f.getAsJsonObject();
				long offset = field.get("offset").getAsLong();
				if (offset < MIN_OFFSET || !field.get("declared_in").getAsString().equals(name)) {
					continue; // skip shared base-class fields: they do not identify the ability
				}
				fieldsAt.computeIfAbsent(offset, k -> new ArrayList<>())
					.add(new String[] { name, field.get("name").getAsString() });
			}
		}

		Set<Function> nearMovement = movementCallees(read(args[1]));
		// function -> struct -> fields
		Map<Function, Map<String, Set<String>>> hits = new HashMap<>();
		Listing listing = currentProgram.getListing();
		InstructionIterator instructions = listing.getInstructions(true);
		long scanned = 0;
		while (instructions.hasNext() && !monitor.isCancelled()) {
			Instruction instruction = instructions.next();
			scanned++;
			for (int op = 0; op < instruction.getNumOperands(); op++) {
				if (!OperandType.isDynamic(instruction.getOperandType(op))) {
					continue;
				}
				Object[] objects = instruction.getOpObjects(op);
				if (stackRelative(objects)) {
					continue; // large stack frames reuse the same displacements
				}
				for (Object object : objects) {
					if (!(object instanceof Scalar)) {
						continue;
					}
					List<String[]> candidates = fieldsAt.get(((Scalar) object).getUnsignedValue());
					if (candidates == null) {
						continue;
					}
					Function function = listing.getFunctionContaining(instruction.getAddress());
					if (function == null) {
						continue;
					}
					for (String[] candidate : candidates) {
						hits.computeIfAbsent(function, k -> new HashMap<>())
							.computeIfAbsent(candidate[0], k -> new TreeSet<>()).add(candidate[1]);
					}
				}
			}
		}

		JsonArray rows = new JsonArray();
		List<Map.Entry<Function, Map<String, Set<String>>>> ranked = new ArrayList<>(hits.entrySet());
		ranked.sort((a, b) -> Integer.compare(best(b.getValue()), best(a.getValue())));
		for (Map.Entry<Function, Map<String, Set<String>>> entry : ranked) {
			if (best(entry.getValue()) < MIN_FIELDS) {
				continue;
			}
			Function function = entry.getKey();
			JsonObject row = new JsonObject();
			row.addProperty("function", function.getName());
			row.addProperty("rva", "0x" + Long.toHexString(
				function.getEntryPoint().subtract(currentProgram.getImageBase())));
			row.addProperty("bytes", function.getBody().getNumAddresses());
			row.addProperty("reachable_from_movement_process", nearMovement.contains(function));
			JsonObject structs = new JsonObject();
			for (Map.Entry<String, Set<String>> s : entry.getValue().entrySet()) {
				JsonArray names = new JsonArray();
				s.getValue().forEach(names::add);
				structs.add(s.getKey(), names);
			}
			row.add("fields", structs);
			rows.add(row);
		}
		JsonObject report = new JsonObject();
		report.addProperty("program", currentProgram.getName());
		report.addProperty("instructions_scanned", scanned);
		report.addProperty("min_offset", MIN_OFFSET);
		report.addProperty("call_depth_from_movement_process", CALL_DEPTH);
		report.addProperty("caveat", "Displacement matches are leads; confirm each in the decompiler.");
		report.add("functions", rows);
		try (FileWriter writer = new FileWriter(args[2])) {
			new GsonBuilder().setPrettyPrinting().create().toJson(report, writer);
		}
		println("Probe report: " + rows.size() + " functions -> " + args[2]);
	}

	private static boolean stackRelative(Object[] objects) {
		for (Object object : objects) {
			if (object instanceof Register) {
				String name = ((Register) object).getName().toUpperCase();
				if (name.equals("RSP") || name.equals("RBP") || name.equals("ESP") || name.equals("EBP")) {
					return true;
				}
			}
		}
		return false;
	}

	private static int best(Map<String, Set<String>> byStruct) {
		int best = 0;
		for (Set<String> fields : byStruct.values()) {
			best = Math.max(best, fields.size());
		}
		return best;
	}

	private Set<Function> movementCallees(JsonObject labels) {
		Set<Function> seen = new HashSet<>();
		String module = currentProgram.getName().toLowerCase();
		for (JsonElement element : labels.getAsJsonArray("labels")) {
			JsonObject label = element.getAsJsonObject();
			if (!label.get("id").getAsString().equals("movement.process")
				|| !label.get("module").getAsString().equals(module)) {
				continue;
			}
			Address address = currentProgram.getImageBase().add(label.get("rva").getAsLong());
			Function root = getFunctionAt(address);
			if (root == null) {
				continue;
			}
			Deque<Function> frontier = new ArrayDeque<>(List.of(root));
			seen.add(root);
			for (int depth = 0; depth < CALL_DEPTH; depth++) {
				Deque<Function> next = new ArrayDeque<>();
				for (Function function : frontier) {
					for (Function callee : function.getCalledFunctions(monitor)) {
						if (seen.add(callee)) {
							next.add(callee);
						}
					}
				}
				frontier = next;
			}
		}
		return seen;
	}

	private static JsonObject read(String path) throws Exception {
		try (FileReader reader = new FileReader(path)) {
			return JsonParser.parseReader(reader).getAsJsonObject();
		}
	}
}
