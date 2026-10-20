// Apply Modlock signature labels and schema-dump structs to the open program.
// Arguments: labels.json types.json build-label. Written by static_analysis.py.
// Labels name functions only; their semantics remain to be verified.
// @category DeadlockMovement

import java.io.FileReader;
import java.util.ArrayList;
import java.util.List;

import com.google.gson.JsonArray;
import com.google.gson.JsonElement;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;

import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.data.*;
import ghidra.program.model.listing.*;
import ghidra.program.model.symbol.SourceType;

public class ApplyResearchAnnotations extends GhidraScript {

	private CategoryPath category;
	private DataTypeManager types;

	@Override
	protected void run() throws Exception {
		String[] args = getScriptArgs();
		if (args.length != 3) {
			throw new IllegalArgumentException("Usage: labels.json types.json build");
		}
		JsonObject labels = read(args[0]);
		JsonObject structs = read(args[1]);
		category = new CategoryPath("/DeadlockSchema/" + args[2]);
		types = currentProgram.getDataTypeManager();
		int created = applyTypes(structs.getAsJsonArray("structs"));
		int named = applyLabels(labels.getAsJsonArray("labels"));
		println("Applied " + created + " structs and " + named + " labels for build " + args[2]);
	}

	private static JsonObject read(String path) throws Exception {
		try (FileReader reader = new FileReader(path)) {
			return JsonParser.parseReader(reader).getAsJsonObject();
		}
	}

	private DataType scalar(String kind) {
		switch (kind) {
			case "float": return FloatDataType.dataType;
			case "double": return DoubleDataType.dataType;
			case "bool": return BooleanDataType.dataType;
			case "int8": return SignedByteDataType.dataType;
			case "uint8": return ByteDataType.dataType;
			case "int16": return ShortDataType.dataType;
			case "uint16": return UnsignedShortDataType.dataType;
			case "int32": return IntegerDataType.dataType;
			case "uint32": return UnsignedIntegerDataType.dataType;
			case "int64": return LongLongDataType.dataType;
			case "uint64": return UnsignedLongLongDataType.dataType;
			case "pointer": return PointerDataType.dataType;
			default: return Undefined1DataType.dataType;
		}
	}

	private int applyTypes(JsonArray structs) {
		// Create empty shells first so pointer parameters can refer to any of them.
		for (JsonElement element : structs) {
			JsonObject struct = element.getAsJsonObject();
			StructureDataType shell = new StructureDataType(category,
				struct.get("name").getAsString(), struct.get("size").getAsInt(), types);
			types.addDataType(shell, DataTypeConflictHandler.REPLACE_HANDLER);
		}
		for (JsonElement element : structs) {
			JsonObject struct = element.getAsJsonObject();
			Structure target = (Structure) types.getDataType(category, struct.get("name").getAsString());
			target.deleteAll();
			target.setLength(struct.get("size").getAsInt());
			target.setDescription(struct.get("provenance").getAsString());
			for (JsonElement fieldElement : struct.getAsJsonArray("fields")) {
				JsonObject field = fieldElement.getAsJsonObject();
				String kind = field.get("kind").getAsString();
				int count = field.get("count").getAsInt();
				int size = field.get("size").getAsInt();
				DataType base = scalar(kind);
				DataType type = count == 1 && !kind.equals("bytes") ? base
					: new ArrayDataType(base, count, base.getLength(), types);
				String comment = field.has("schema_type")
					? field.get("schema_type").getAsString() + " (" + field.get("declared_in").getAsString() + ")"
					: null;
				target.replaceAtOffset(field.get("offset").getAsInt(), type, size,
					field.get("name").getAsString(), comment);
			}
		}
		return structs.size();
	}

	private DataType parameterType(String name) {
		boolean pointer = name.endsWith("*");
		String bare = pointer ? name.substring(0, name.length() - 1) : name;
		DataType type = types.getDataType(category, bare);
		if (type == null) {
			type = Undefined8DataType.dataType;
			pointer = false;
		}
		return pointer ? new PointerDataType(type, types) : type;
	}

	private int applyLabels(JsonArray labels) throws Exception {
		String module = currentProgram.getName().toLowerCase();
		Address base = currentProgram.getImageBase();
		int named = 0;
		for (JsonElement element : labels) {
			JsonObject label = element.getAsJsonObject();
			if (!label.get("module").getAsString().equals(module)) {
				continue;
			}
			Address address = base.add(label.get("rva").getAsLong());
			String name = label.get("name").getAsString();
			Function function = getFunctionAt(address);
			if (function == null) {
				function = createFunction(address, name);
			}
			if (function == null) {
				// Some signatures resolve to an instruction inside a function.
				createLabel(address, name, true, SourceType.USER_DEFINED);
			}
			else {
				function.setName(name, SourceType.USER_DEFINED);
				if (label.has("params") && label.get("params").isJsonArray()) {
					List<ParameterImpl> params = new ArrayList<>();
					for (JsonElement p : label.getAsJsonArray("params")) {
						JsonArray pair = p.getAsJsonArray();
						params.add(new ParameterImpl(pair.get(0).getAsString(),
							parameterType(pair.get(1).getAsString()), currentProgram));
					}
					function.replaceParameters(Function.FunctionUpdateType.DYNAMIC_STORAGE_ALL_PARAMS,
						true, SourceType.USER_DEFINED, params.toArray(new ParameterImpl[0]));
				}
			}
			StringBuilder comment = new StringBuilder("Modlock signature ")
				.append(label.get("id").getAsString());
			if (label.has("shape") && !label.get("shape").isJsonNull()) {
				comment.append("\nShape: ").append(label.get("shape").getAsString());
			}
			if (label.has("note") && !label.get("note").isJsonNull()) {
				comment.append("\n").append(label.get("note").getAsString());
			}
			comment.append("\nUnverified on this build beyond the byte pattern.");
			setPlateComment(address, comment.toString());
			named++;
		}
		return named;
	}
}
