// 原型模拟数据与纯函数（与真实模型字段对齐：#id/姓名/身高/性别/前排需求）
export function makeStudents(n = 240) {
  const sur = ['王','李','张','刘','陈','杨','赵','黄','周','吴','徐','孙','胡','朱','高','林','何','郭','马','罗'];
  const given = ['伟','芳','娜','敏','静','磊','军','洋','勇','艳','杰','涛','明','超','秀英','霞','平','刚','桂英','文博'];
  const out = [];
  for (let i = 0; i < n; i++) {
    const given2 = i >= 20 ? given[(i * 13 + 3) % given.length] : '';
    out.push({
      id: `stu-${String(i).padStart(4, '0')}`,
      name: sur[i % sur.length] + given[(i * 7) % given.length] + given2,
      height: 150 + ((i * 7) % 40),
      gender: i % 2 === 0 ? 'male' : 'female',
      needsFrontRow: i % 30 === 0,
    });
  }
  return out;
}

export const VENUES = [
  { id: 'demo-01', name: '演示教室', type: 'Grid', rows: 8, columns: 8, seatsPerDesk: 2, seats: 64, aisleAfter: 4, hasPodium: true },
  { id: 'demo-02', name: '阶梯教室 A', type: 'Grid', rows: 10, columns: 12, seatsPerDesk: 2, seats: 120, aisleAfter: 6, hasPodium: true },
  { id: 'demo-05', name: '大教室 · 300 座', type: 'Grid', rows: 15, columns: 20, seatsPerDesk: 2, seats: 300, aisleAfter: 10, hasPodium: true },
  { id: 'demo-03', name: '环形研讨室', type: 'Polar', rings: 4, perRing: 12, seats: 48, seatsPerDesk: 1, hasPodium: false },
  { id: 'demo-04', name: '自由布局 · 实验室', type: 'Freeform', seats: 36, seatsPerDesk: 4, hasPodium: true },
];

export const ROSTERS = [
  { id: 'roster-01', name: '演示名单', count: 240, importedAt: '2026-10-01' },
  { id: 'roster-02', name: '高一(3)班', count: 46, importedAt: '2026-09-28' },
];

export const STRATEGIES = [
  { id: 'fixed', name: '固定座位', desc: '锁定指定学生的座位', priority: 100, enabled: true, kind: 'independent', status: '已执行', detail: '锁定 4 个座位' },
  { id: 'front', name: '前排轮换', desc: '需要前排的学生按分数轮换', priority: 50, enabled: true, kind: 'independent', status: '已执行', detail: '8 人 · 已打乱' },
  { id: 'random', name: '随机填充', desc: '其余学生随机填充空位', priority: 1, enabled: true, kind: 'independent', status: '已执行', detail: '60 人 · 重掷 3 次' },
  { id: 'deskmate', name: '同桌分组', desc: '按配置让指定学生同桌', priority: 50, enabled: true, kind: 'dependent', status: '已执行', detail: '依赖随机填充' },
  { id: 'gender', name: '性别限制座位', desc: '按座位性别限制分配', priority: 45, enabled: true, kind: 'dependent', status: '已执行', detail: '依赖随机填充' },
  { id: 'norepeat', name: '同桌不重复', desc: '避免与近期同桌重复', priority: 40, enabled: true, kind: 'dependent', status: '2 条消息', detail: '依赖随机填充' },
  { id: 'defrag', name: '空位收敛', desc: '把后排零散学生前移填补空位', priority: 0, enabled: false, kind: 'independent', status: '未启用', detail: '执行顺序最后' },
];

export const MESSAGES = [
  { id: 'm1', level: 'warn', strategy: '同桌不重复', text: '第 6 排的两位同学曾在上次排座同桌，已强制安排并记录。' },
  { id: 'm2', level: 'warn', strategy: '性别限制座位', text: '第 3 排限制座位不足，已将 1 名学生安排到普通空位。' },
];

export const SNAPSHOTS = [
  { id: 's1', name: '第 8 周 · 期中调整', venue: '演示教室', date: '2026-10-01 14:20', assigned: 64, total: 64, note: '换座' },
  { id: 's2', name: '第 6 周 · 常规轮换', venue: '演示教室', date: '2026-09-17 09:05', assigned: 64, total: 64, note: '轮换' },
  { id: 's3', name: '开学初排', venue: '演示教室', date: '2026-09-01 08:30', assigned: 63, total: 64, note: '初始' },
  { id: 's4', name: '上学期期末', venue: '阶梯教室 A', date: '2026-06-28 16:40', assigned: 120, total: 120, note: '考场' },
];

// 简易座位生成：返回 {id, row, col, studentId}
export function buildSeats(venue) {
  if (venue.type !== 'Grid') {
    const seats = [];
    for (let i = 0; i < venue.seats; i++) seats.push({ id: `p-${i}`, row: Math.floor(i / venue.perRing) + 1, col: (i % (venue.perRing || 12)) + 1, studentId: null });
    return seats;
  }
  const seats = [];
  for (let r = 1; r <= venue.rows; r++)
    for (let c = 1; c <= venue.columns; c++) seats.push({ id: `s-${r}-${c}`, row: r, col: c, studentId: null });
  return seats;
}

// 模拟生成：固定前 4 座 + 随机填充
export function generate(venue, students) {
  const seats = buildSeats(venue);
  let si = 0;
  for (const s of seats) {
    if (s.row === 1 && s.col <= 4) s.fixed = true;
    if (si < students.length) s.studentId = students[si++].id;
  }
  return seats;
}
