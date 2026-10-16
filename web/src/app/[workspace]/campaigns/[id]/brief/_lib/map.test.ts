import { expect, test } from "vitest";
import { proof } from "../fixtures.test-data";
import { bounds, coordinates, ranked } from "./map";

const region = proof.plan!.holdouts[0];
test("projects real coordinates in the same space as the land outline", () => { expect(coordinates({ ...region, latitude: "19", longitude: "73" })).toEqual({ x: 253, y: 71 }); });
test.each([{ latitude: null }, { longitude: null }, { latitude: 91 }, { longitude: -181 }, { longitude: "bad" }])("never invents missing or invalid locations: %j", values => { expect(coordinates({ ...region, ...values })).toBeNull(); });
test("fits all region points with padding and keeps bounds on the globe", () => {
  const regions = [...proof.plan!.testRegions, ...proof.plan!.holdouts]; const box = bounds(regions);
  for (const region of regions) { const point = coordinates(region)!; expect(point.x).toBeGreaterThan(box.x); expect(point.x).toBeLessThan(box.x + box.width); expect(point.y).toBeGreaterThan(box.y); expect(point.y).toBeLessThan(box.y + box.height); }
  expect(bounds([])).toEqual({ x: 0, y: 0, width: 360, height: 180 });
  const world = bounds([{ ...region, latitude: 90, longitude: 180 }, { ...region, latitude: -90, longitude: -180 }]); expect(world).toEqual({ x: 0, y: 0, width: 360, height: 180 });
});
test("ranks known match scores before missing scores and deduplicates candidates", () => {
  expect(ranked([{ ...region, id: "c", matchScore: null }, { ...region, id: "b", matchScore: ".9" }, { ...region, id: "a", matchScore: ".8" }, { ...region, id: "b", matchScore: ".9" }]).map(region => region.id)).toEqual(["b", "a", "c"]);
});
