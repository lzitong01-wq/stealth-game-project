/**
 * 火柴人骨架定义。
 *
 * 设计约束：
 * 1. 纯数据 + 纯函数，不依赖引擎，方便单测与跨项目复用。
 * 2. 关节角度用「欧拉角（度）」表达，顺序 X→Y→Z。
 * 3. BONE_OFFSET 是「从父关节到本关节」的局部偏移向量（米），
 *    在父关节的局部空间里。这样腿朝下、手朝外都能直接用数据表达，
 *    不必依赖「骨骼沿 +Y」的隐式约定。
 *
 * FK 约定：
 *   worldRot[j]  = worldRot[parent] * localRot[j]
 *   worldPos[j]  = worldPos[parent] + worldRot[parent] * BONE_OFFSET[j]
 * 即：本关节的旋转只影响它的子关节，自己的位置由父关节决定。
 */
export const JOINTS = [
    'pelvis',
    'spine',
    'chest',
    'neck',
    'head',
    'shoulderL',
    'elbowL',
    'handL',
    'shoulderR',
    'elbowR',
    'handR',
    'hipL',
    'kneeL',
    'ankleL',
    'footL',
    'hipR',
    'kneeR',
    'ankleR',
    'footR',
];
/** 父关节。null 表示骨架根节点。 */
export const JOINT_PARENT = {
    pelvis: null,
    spine: 'pelvis',
    chest: 'spine',
    neck: 'chest',
    head: 'neck',
    shoulderL: 'chest',
    elbowL: 'shoulderL',
    handL: 'elbowL',
    shoulderR: 'chest',
    elbowR: 'shoulderR',
    handR: 'elbowR',
    hipL: 'pelvis',
    kneeL: 'hipL',
    ankleL: 'kneeL',
    footL: 'ankleL',
    hipR: 'pelvis',
    kneeR: 'hipR',
    ankleR: 'kneeR',
    footR: 'ankleR',
};
/**
 * 父关节局部空间里的偏移（米）。按 1.75m 身高比例取整。
 * 约定：X 右、Y 上、Z 前。
 */
export const BONE_OFFSET = {
    // 根：骨盆离地高度
    pelvis: [0, 0.95, 0],
    spine: [0, 0.18, 0],
    chest: [0, 0.26, 0],
    neck: [0, 0.09, 0],
    head: [0, 0.22, 0],
    // 左臂：肩在胸腔左上方，肘向外侧，手向外延伸
    shoulderL: [0.17, 0.16, 0],
    elbowL: [0.28, 0, 0],
    handL: [0.25, 0, 0],
    // 右臂
    shoulderR: [-0.17, 0.16, 0],
    elbowR: [-0.28, 0, 0],
    handR: [-0.25, 0, 0],
    // 左腿：髋向侧，膝向下，踝向下，脚向前
    hipL: [0.1, -0.06, 0],
    kneeL: [0, -0.44, 0],
    ankleL: [0, -0.42, 0],
    footL: [0, -0.06, 0.16],
    // 右腿
    hipR: [-0.1, -0.06, 0],
    kneeR: [0, -0.44, 0],
    ankleR: [0, -0.42, 0],
    footR: [0, -0.06, 0.16],
};
/** 左右镜像的关节对，生成对称姿态时用。 */
export const MIRROR_PAIRS = [
    ['shoulderL', 'shoulderR'],
    ['elbowL', 'elbowR'],
    ['handL', 'handR'],
    ['hipL', 'hipR'],
    ['kneeL', 'kneeR'],
    ['ankleL', 'ankleR'],
    ['footL', 'footR'],
];
/** 绘制火柴人时连线的骨段（父 → 子）。 */
export const BONE_SEGMENTS = JOINTS.filter((j) => JOINT_PARENT[j] !== null).map((j) => [JOINT_PARENT[j], j]);
/** FK 求解顺序：父关节一定排在子关节之前。 */
export const FK_ORDER = (() => {
    const out = [];
    const visit = (j) => {
        out.push(j);
        for (const child of JOINTS) {
            if (JOINT_PARENT[child] === j)
                visit(child);
        }
    };
    visit('pelvis');
    return out;
})();
