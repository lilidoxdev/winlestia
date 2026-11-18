.pragma library
// Synchronise un ListModel avec une liste de lignes identifiées par `key` : ajoute, déplace, retire et ne met à jour que les
// champs modifiés. Aucun délégué n'est détruit tant que la liste d'identifiants ne change pas.
function sync(model, src, key) {
    const keep = {};
    for (const r of src)
        keep[r[key]] = true;
    for (let i = model.count - 1; i >= 0; i--)
        if (!keep[model.get(i)[key]])
            model.remove(i);
    for (let i = 0; i < src.length; i++) {
        const r = src[i];
        let at = -1;
        for (let k = i; k < model.count; k++)
            if (model.get(k)[key] === r[key]) {
                at = k;
                break;
            }
        if (at < 0) {
            model.insert(i, r);
            continue;
        }
        if (at !== i)
            model.move(at, i, 1);
        for (const f in r)
            if (model.get(i)[f] !== r[f])
                model.setProperty(i, f, r[f]);
    }
}
